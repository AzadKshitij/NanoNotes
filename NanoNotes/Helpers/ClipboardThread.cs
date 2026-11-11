// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Graphics.Imaging;

namespace notes;

/// <summary>
/// A single background STA thread that owns clipboard access. The WinRT clipboard
/// API hangs when called from a plain MTA thread, so every read and write is
/// marshalled here. One thread for the whole extension keeps the cost at one
/// idle wait handle.
/// </summary>
internal sealed partial class ClipboardThreadQueue : IDisposable
{
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
/// clipboard's STA requirements. Text uses the toolkit helper; images are
/// decoded to a DIB, the format Paint and other classic apps paste from.
/// </summary>
internal static partial class ClipboardWriter
{
    private const uint CF_DIB = 8;
    private const uint GMEM_MOVEABLE = 0x0002;

    private static readonly ClipboardThreadQueue _queue = new();

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
                SetDibOnClipboard(CreateDib(path));
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

        if (!finished.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("The clipboard did not accept the image in time.");
        }

        if (failure is not null)
        {
            throw new InvalidOperationException("Could not copy the image to the clipboard.", failure);
        }
    }

    private static byte[] CreateDib(string path)
    {
        using var file = File.OpenRead(path);
        using var stream = file.AsRandomAccessStream();
        var decoder = BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();

        var width = (int)decoder.PixelWidth;
        var height = (int)decoder.PixelHeight;
        var provider = decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage).AsTask().GetAwaiter().GetResult();
        var pixels = provider.DetachPixelData();
        var stride = width * 4;

        if (width <= 0 || height <= 0 || pixels.Length < stride * height)
        {
            throw new InvalidDataException("The image has no usable pixels.");
        }

        const int headerSize = 40;
        var dib = new byte[headerSize + (stride * height)];
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(0), headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height);
        BinaryPrimitives.WriteInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteInt16LittleEndian(dib.AsSpan(14), 32);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(16), 0);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(20), stride * height);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(24), 0);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(28), 0);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(32), 0);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(36), 0);

        // DIB rows run bottom-up.
        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(pixels, y * stride, dib, headerSize + ((height - 1 - y) * stride), stride);
        }

        return dib;
    }

    private static void SetDibOnClipboard(byte[] dib)
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            throw new InvalidOperationException($"Could not open the clipboard ({Marshal.GetLastPInvokeError()}).");
        }

        try
        {
            if (!EmptyClipboard())
            {
                throw new InvalidOperationException($"Could not clear the clipboard ({Marshal.GetLastPInvokeError()}).");
            }

            var handle = GlobalAlloc(GMEM_MOVEABLE, (nuint)dib.Length);
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Could not allocate clipboard memory ({Marshal.GetLastPInvokeError()}).");
            }

            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero)
            {
                _ = GlobalFree(handle);
                throw new InvalidOperationException($"Could not lock clipboard memory ({Marshal.GetLastPInvokeError()}).");
            }

            try
            {
                Marshal.Copy(dib, 0, pointer, dib.Length);
            }
            finally
            {
                _ = GlobalUnlock(handle);
            }

            if (SetClipboardData(CF_DIB, handle) == IntPtr.Zero)
            {
                _ = GlobalFree(handle);
                throw new InvalidOperationException($"Could not put the image on the clipboard ({Marshal.GetLastPInvokeError()}).");
            }
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(IntPtr newOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SetClipboardData(uint format, IntPtr memory);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalLock(IntPtr memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalFree(IntPtr memory);
}
