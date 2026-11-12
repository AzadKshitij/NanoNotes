// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace notes;

/// <summary>
/// Remembers which window was in front before the Command Palette took the
/// focus. The palette floats on top of the app the user was working in, so
/// pasting a note back into that app means finding that app first.
///
/// The list is filled by a WinEvent hook. The callback is delivered to the
/// thread that installed the hook, so that thread needs a message pump.
/// </summary>
internal sealed partial class ForegroundTracker : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;

    /// <summary>
    /// How many foreground changes to remember. A handful is enough to find the
    /// window behind the palette even when the user clicked somewhere else
    /// while the palette was open.
    /// </summary>
    private const int MaxHistory = 16;

    /// <summary>
    /// The palette's own process. It is skipped: typing into it would put the
    /// text in its search box instead of in the app the user was working in.
    /// </summary>
    private const string PaletteProcessName = "Microsoft.CmdPal";

    private static readonly Lazy<ForegroundTracker> _instance = new(() => new ForegroundTracker());

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly List<IntPtr> _history = [];
    private readonly object _gate = new();

    private WinEventCallback? _callback;
    private IntPtr _hook;
    private bool _disposed;

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
    /// The window that was in front before <paramref name="current"/>, as long as
    /// it is still open.
    /// </summary>
    public IntPtr WindowBefore(IntPtr current)
    {
        lock (_gate)
        {
            foreach (var window in _history)
            {
                if (window != current && !IsPaletteWindow(window) && IsWindow(window))
                {
                    return window;
                }
            }
        }

        return IntPtr.Zero;
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

        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }

        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    private void Run()
    {
        try
        {
            _callback = OnForegroundChanged;
            _hook = SetWinEventHook(
                EventSystemForeground,
                EventSystemForeground,
                IntPtr.Zero,
                _callback,
                0,
                0,
                WineventOutOfContext | WineventSkipOwnProcess);

            var current = GetForegroundWindow();
            if (current != IntPtr.Zero)
            {
                _history.Add(current);
            }

            _ready.Set();

            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception ex)
        {
            ExtensionHost.LogMessage("notes foreground tracker failed: " + ex.Message);
        }
        finally
        {
            _ready.Set();
        }
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr window, int idObject, int idChild, uint threadId, uint time)
    {
        if (window == IntPtr.Zero)
        {
            return;
        }

        lock (_gate)
        {
            _history.Remove(window);
            _history.Insert(0, window);

            if (_history.Count > MaxHistory)
            {
                _history.RemoveAt(_history.Count - 1);
            }
        }
    }

    private delegate void WinEventCallback(IntPtr hook, uint eventType, IntPtr window, int idObject, int idChild, uint threadId, uint time);

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr moduleHandle,
        WinEventCallback callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(IntPtr window);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessage(out Message message, IntPtr window, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref Message message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial IntPtr DispatchMessage(ref Message message);
}
