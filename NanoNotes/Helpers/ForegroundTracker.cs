// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace notes;

/// <summary>
/// Remembers the app the user is working in, and the control that has the
/// caret in it.
///
/// The window in front is not the same thing as the box with the caret in it.
/// The palette takes the focus while it is open, and the user can click into
/// another app in the meantime.
///
/// Every thread keeps its own focus, even when it is in the background. So the
/// caret of the app the user came from can be read while the palette is in
/// front: it is the focus of that app's thread.
///
/// The focus is polled rather than watched. The focus events are not
/// delivered to an extension, but the focus itself can be read at any time.
/// </summary>
internal sealed partial class ForegroundTracker : IDisposable
{
    /// <summary>
    /// The palette's own process. It is skipped: typing into one of those
    /// would put the text in its search box instead of in the app the user was
    /// working in.
    /// </summary>
    private const string PaletteProcessName = "Microsoft.CmdPal";

    /// <summary>
    /// How often to look at the focus. A tenth of a second is quick enough
    /// that the app is never missed, and slow enough to cost nothing.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private static readonly Lazy<ForegroundTracker> _instance = new(() => new ForegroundTracker());

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly object _gate = new();

    private bool _disposed;

    /// <summary>
    /// The app the user is working in: the last window in front that was not
    /// the palette.
    /// </summary>
    private IntPtr _appWindow;

    public ForegroundTracker()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "notes foreground tracker",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// The tracker starts with the app that is in front right now, which is the
    /// app the user was in when the palette came up.
    /// </summary>
    public static ForegroundTracker Instance => _instance.Value;

    /// <summary>
    /// The app the user is working in: the last window in front that was not
    /// the palette.
    /// </summary>
    public IntPtr AppWindow()
    {
        lock (_gate)
        {
            return _appWindow;
        }
    }

    /// <summary>
    /// The control that has the caret in the app the user is working in. The
    /// focus of a thread stays with that thread, so this can be read while the
    /// palette has the focus.
    /// </summary>
    public IntPtr FocusedControl()
    {
        var app = AppWindow();
        if (app == IntPtr.Zero || !IsWindow(app))
        {
            return IntPtr.Zero;
        }

        var thread = GetWindowThreadProcessId(app, out _);
        if (thread == 0 || !TryGetFocus(thread, out var focus))
        {
            return IntPtr.Zero;
        }

        if (IsPaletteWindow(focus))
        {
            return IntPtr.Zero;
        }

        return focus;
    }

    /// <summary>
    /// True for the Command Palette's own windows. Typing into one of those
    /// would put the text in its search box instead of in the app the user was
    /// working in.
    /// </summary>
    public static bool IsPaletteWindow(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName.StartsWith(PaletteProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    private void Run()
    {
        try
        {
            _ready.Set();

            while (!_disposed)
            {
                Poll();

                Thread.Sleep(PollInterval);
            }
        }
        catch (Exception ex)
        {
            ExtensionHost.LogMessage("notes foreground tracker failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Notes the app in front. The palette is skipped: it is not the app the
    /// user is working in.
    /// </summary>
    private void Poll()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || IsPaletteWindow(foreground))
        {
            return;
        }

        lock (_gate)
        {
            if (_appWindow == foreground)
            {
                return;
            }

            _appWindow = foreground;
        }
    }

    /// <summary>
    /// Reads the focus of the given thread. The size field has to be set
    /// before the call, so the struct cannot be passed as an out parameter.
    /// </summary>
    private static bool TryGetFocus(uint threadId, out IntPtr focus)
    {
        focus = IntPtr.Zero;
        if (threadId == 0)
        {
            return false;
        }

        var info = new GuiThreadInfo
        {
            Size = Marshal.SizeOf<GuiThreadInfo>(),
        };

        if (!GetGUIThreadInfo(threadId, ref info))
        {
            return false;
        }

        focus = info.Focus;
        return focus != IntPtr.Zero;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(IntPtr window);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public IntPtr Active;
        public IntPtr Focus;
        public IntPtr Capture;
        public IntPtr MenuOwner;
        public IntPtr MoveSize;
        public IntPtr Caret;
        public Rect CaretRect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
