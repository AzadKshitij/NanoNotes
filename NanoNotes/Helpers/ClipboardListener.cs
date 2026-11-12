// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace notes;

/// <summary>
/// Listens for clipboard changes with the Win32 clipboard listener API.
///
/// The WinRT <c>Clipboard.ContentChanged</c> event never fires for an extension
/// process: it needs a message pump on a thread that a COM server does not run.
/// A message-only window on a dedicated thread is the reliable way to hear about
/// every copy.
/// </summary>
internal sealed partial class ClipboardListener : IDisposable
{
    private const uint WM_CLIPBOARDUPDATE = 0x031D;
    private const uint WM_QUIT = 0x0012;
    private const int MessageOnlyWindow = -3;
    private const string ClassName = "notes.clipboard.listener";

    private delegate IntPtr WindowProcedureCallback(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private readonly Action _onUpdate;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private uint _threadId;
    private bool _disposed;

    // Keep the callback delegate alive for the life of the process: the window
    // class holds a raw pointer to it.
    private static readonly WindowProcedureCallback _procedure = WindowProcedure;
    private static ClipboardListener? _current;

    public ClipboardListener(Action onUpdate)
    {
        _onUpdate = onUpdate;

        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "notes clipboard listener",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_ready.Wait(TimeSpan.FromSeconds(10)))
        {
            ExtensionHost.LogMessage("notes clipboard listener was not ready in time");
        }
    }

    private void Run()
    {
        _current = this;

        var instance = GetModuleHandle(null);
        var classNamePointer = Marshal.StringToHGlobalUni(ClassName);

        try
        {
            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                Procedure = Marshal.GetFunctionPointerForDelegate(_procedure),
                Instance = instance,
                ClassName = classNamePointer,
            };

            _ = RegisterClass(ref windowClass);

            var window = CreateWindowEx(
                0,
                classNamePointer,
                IntPtr.Zero,
                0,
                0, 0, 0, 0,
                new IntPtr(MessageOnlyWindow),
                IntPtr.Zero,
                instance,
                IntPtr.Zero);

            if (window == IntPtr.Zero)
            {
                return;
            }

            _threadId = GetCurrentThreadId();
            _ = AddClipboardFormatListener(window);
            _ready.Set();

            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                _ = TranslateMessage(ref message);
                _ = DispatchMessage(ref message);
            }

            _ = RemoveClipboardFormatListener(window);
            _ = DestroyWindow(window);
        }
        finally
        {
            Marshal.FreeHGlobal(classNamePointer);
            _ready.Set();
        }
    }

    private static IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WM_CLIPBOARDUPDATE)
        {
            try
            {
                _current?._onUpdate();
            }
            catch (Exception ex)
            {
                ExtensionHost.LogMessage("notes clipboard listener: " + ex.Message);
            }
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // The window may never have been created: the thread that creates it can
        // take longer than the constructor's wait. Posting to a thread id of
        // zero does nothing, and joining it then waits forever.
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        if (!_thread.Join(TimeSpan.FromSeconds(5)))
        {
            ExtensionHost.LogMessage("notes clipboard listener did not stop in time");
            return;
        }

        _current = null;
        _ready.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public IntPtr Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public IntPtr MenuName;
        public IntPtr ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Id;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW")]
    private static partial ushort RegisterClass(ref WindowClass windowClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW")]
    private static partial IntPtr CreateWindowEx(
        uint style,
        IntPtr className,
        IntPtr windowName,
        uint windowStyle,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AddClipboardFormatListener(IntPtr window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveClipboardFormatListener(IntPtr window);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessage(out Message message, IntPtr window, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref Message message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial IntPtr DispatchMessage(ref Message message);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr GetModuleHandle(string? moduleName);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);
}
