// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace notes;

/// <summary>
/// Watches the clipboard and records every copied text or image into
/// <see cref="ClipboardStore"/>. Runs for as long as the extension is loaded,
/// which is as long as Command Palette keeps it alive.
/// </summary>
internal sealed partial class ClipboardMonitor : IDisposable
{
    /// <summary>
    /// Images larger than this are skipped: a single 50 MB screenshot would
    /// dominate the history folder.
    /// </summary>
    private const long MaxImageBytes = 25L * 1024 * 1024;

    /// <summary>
    /// Captured images are downscaled to this on their longest side. A 4K
    /// screenshot stays perfectly readable and drops from ~15 MB to ~1 MB.
    /// </summary>
    private const int MaxImageDimension = 1920;

    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(15);

    private readonly ClipboardStore _store;
    private readonly SettingsManager _settings;
    private readonly ClipboardThreadQueue _queue = new();
    private readonly ClipboardListener _listener;
    private readonly Timer _pruneTimer;
    private bool _disposed;

    public ClipboardMonitor(ClipboardStore store, SettingsManager settings)
    {
        _store = store;
        _settings = settings;

        _pruneTimer = new Timer(_ => _store.Prune(_settings.RetentionDays), null, PruneInterval, PruneInterval);

        _listener = new ClipboardListener(() => OnContentChanged(null, EventArgs.Empty));
        DebugLog.Write("monitor started");
    }

    private void OnContentChanged(object? sender, object e)
    {
        DebugLog.Write("content changed");
        if (_disposed)
        {
            return;
        }

        // The event can fire while a previous read is still in flight; the
        // queue serialises them so two copies never race the clipboard.
        _queue.EnqueueTask(ReadClipboard);
    }

    private void ReadClipboard()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content is null)
            {
                return;
            }

            if (content.Contains(StandardDataFormats.Text))
            {
                var text = content.GetTextAsync().AsTask().GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var preview = text.Replace("\r", string.Empty).Replace("\n", "\\n");
                    DebugLog.Write("captured text: " + (preview.Length <= 80 ? preview : preview[..80]));
                    _store.AddText(text);
                    return;
                }
            }

            if (_settings.SaveImages && content.Contains(StandardDataFormats.Bitmap))
            {
                var bitmap = content.GetBitmapAsync().AsTask().GetAwaiter().GetResult();
                if (bitmap is not null)
                {
                    using var stream = bitmap.OpenReadAsync().AsTask().GetAwaiter().GetResult();
                    var saved = SaveImageAsync(stream).GetAwaiter().GetResult();
                    if (saved is not null)
                    {
                        DebugLog.Write($"captured image: {saved.Value.Width}x{saved.Value.Height}");
                        _store.AddImage(saved.Value.Path, saved.Value.Width, saved.Value.Height);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ExtensionHost.LogMessage("notes clipboard read failed: " + ex.Message);
        }
    }

    private static async Task<(string Path, int Width, int Height)?> SaveImageAsync(IRandomAccessStream stream)
    {
        try
        {
            if (stream.Size > MaxImageBytes)
            {
                return null;
            }

            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(false);
            using var bitmap = await decoder.GetSoftwareBitmapAsync().AsTask().ConfigureAwait(false);

            var scale = Math.Min(1.0, MaxImageDimension / (double)Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
            var width = Math.Max(1, (int)Math.Round(bitmap.PixelWidth * scale));
            var height = Math.Max(1, (int)Math.Round(bitmap.PixelHeight * scale));

            Directory.CreateDirectory(ClipboardStore.ImageDirectory);

            // Name the file by content: a copy often fires the clipboard event
            // twice (once per format), and re-copying an entry from this very
            // history also reproduces identical bytes. A stable name turns both
            // into no-ops in the store.
            using var png = new MemoryStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png.AsRandomAccessStream()).AsTask().ConfigureAwait(false);
            encoder.SetSoftwareBitmap(bitmap);
            encoder.BitmapTransform.ScaledWidth = (uint)width;
            encoder.BitmapTransform.ScaledHeight = (uint)height;
            await encoder.FlushAsync().AsTask().ConfigureAwait(false);

            var bytes = png.ToArray();
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            var path = Path.Combine(ClipboardStore.ImageDirectory, hash + ".png");
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, bytes);
            }

            return (path, bitmap.PixelWidth, bitmap.PixelHeight);
        }
        catch (Exception ex)
        {
            ExtensionHost.LogMessage("notes clipboard image failed: " + ex.Message);
            return null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _listener.Dispose();
        _pruneTimer.Dispose();
        _queue.Dispose();
    }
}
