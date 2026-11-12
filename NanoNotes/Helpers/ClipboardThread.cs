// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace notes;

/// <summary>
/// A single background STA thread that owns clipboard access. The WinRT clipboard
/// API hangs when called from a plain MTA thread, so every read and write is
/// marshalled here. One thread for the whole extension keeps the cost at one
/// idle wait handle, and it keeps the reader and the writer from fighting over
/// the clipboard at the same time.
/// </summary>
internal sealed partial class ClipboardThreadQueue : IDisposable
{
    /// <summary>
    /// The one clipboard thread of the extension. The monitor and the writer
    /// share it: two threads calling the OLE clipboard at once only buy
    /// intermittent "clipboard busy" failures.
    /// </summary>
    public static ClipboardThreadQueue Shared { get; } = new();

    private readonly Thread _thread;
    private readonly ConcurrentQueue<Action> _tasks = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly CancellationTokenSource _cancellation = new();

    public ClipboardThreadQueue()
    {
        _thread = new Thread(() =>
        {
            _ = CoInitialize(IntPtr.Zero);

            while (!_cancellation.IsCancellationRequested)
            {
                _signal.WaitOne();

                while (_tasks.TryDequeue(out var task))
                {
                    try
                    {
                        task();
                    }
                    catch (Exception ex)
                    {
                        ExtensionHost.LogMessage("notes clipboard thread: " + ex.Message);
                    }
                }
            }

            CoUninitialize();
        });

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.IsBackground = true;
        _thread.Start();
    }

    public void EnqueueTask(Action task)
    {
        _tasks.Enqueue(task);
        _signal.Set();
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _signal.Set();
        _thread.Join();

        _signal.Dispose();
        _cancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitialize(IntPtr pvReserved);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();
}

/// <summary>
/// Writes clipboard content from the UI thread without blocking it on the
/// clipboard's STA requirements. Text uses the toolkit helper; images need the
/// package built here.
/// </summary>
internal static class ClipboardWriter
{
    private static readonly ClipboardThreadQueue _queue = ClipboardThreadQueue.Shared;

    /// <summary>
    /// The bytes of the image currently on the clipboard. The clipboard asks the
    /// source application for the picture when the target application pastes
    /// it, which can happen seconds later and in another process. The stream has
    /// to stay alive until then, so it is kept here instead of being disposed
    /// with the copy.
    /// </summary>
    private static InMemoryRandomAccessStream? _liveStream;

    /// <summary>
    /// Copies an image file back onto the clipboard. Blocks until the clipboard
    /// has it, so the caller can report success or failure.
    /// </summary>
    public static void SetImage(string path)
    {
        using var finished = new ManualResetEventSlim(false);
        Exception? failure = null;

        _queue.EnqueueTask(() =>
        {
            try
            {
                var bytes = File.ReadAllBytes(path);

                // The picture has to live in memory. A stream over the file on
                // disk would be closed by the time the target application asks
                // for the clipboard content, and the paste would come back
                // empty.
                var stream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(bytes);
                    writer.StoreAsync().AsTask().GetAwaiter().GetResult();
                    writer.DetachStream();
                }

                stream.Seek(0);

                var reference = RandomAccessStreamReference.CreateFromStream(stream);
                var package = new DataPackage
                {
                    RequestedOperation = DataPackageOperation.Copy,
                };
                package.SetBitmap(reference);
                Clipboard.SetContent(package);
                Clipboard.Flush();

                _liveStream = stream;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                finished.Set();
            }
        });

        if (!finished.Wait(TimeSpan.FromSeconds(2)))
        {
            throw new TimeoutException("The clipboard did not accept the image in time.");
        }

        if (failure is not null)
        {
            throw new InvalidOperationException("Could not copy the image to the clipboard.", failure);
        }
    }
}
