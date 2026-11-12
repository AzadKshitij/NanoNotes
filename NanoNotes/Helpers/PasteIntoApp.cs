// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace notes;

/// <summary>
/// Puts something on the clipboard and presses Ctrl+V in the box that has the
/// focus, so a note can land in the text box (or canvas) the user was working
/// in. The keystroke is sent as real input, which is what makes it work in
/// apps that draw their own canvas, such as Paint.
/// </summary>
internal static partial class PasteIntoApp
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;

    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyV = 0x56;

    /// <summary>
    /// How long to wait for the palette to close and hand the focus back.
    /// </summary>
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Copies the text to the clipboard and pastes it into the box that has the
    /// focus.
    /// </summary>
    public static void PasteText(string text)
    {
        // The history keeps what the user copied, not what the app pasted, so
        // this copy is marked and skipped when the monitor sees it.
        ClipboardStore.Instance.MarkOwnCopy(text);

        SendPaste(() => ClipboardHelper.SetText(text));
    }

    /// <summary>
    /// Copies the picture to the clipboard and pastes it into the box that has
    /// the focus.
    /// </summary>
    public static void PasteImage(string path)
    {
        // The picture is marked by hand: the monitor works out the file name
        // from the bytes it encodes again, and that name can differ from this
        // one, so the path is not a reliable marker.
        ClipboardStore.Instance.MarkOwnImageCopy();

        // Reading the file and handing it to the clipboard takes a moment. It
        // happens on the paste thread, so the palette does not freeze while it
        // runs.
        SendPaste(() => ClipboardWriter.SetImage(path));
    }

    private static void SendPaste(Action putOnClipboard)
    {
        var palette = GetForegroundWindow();

        // The app the user is working in, and the box with the caret in it.
        // Both are read now, while the palette still has the focus: the focus
        // of a thread stays with that thread, so the caret can still be found.
        var app = ForegroundTracker.Instance.AppWindow();
        var control = ForegroundTracker.Instance.FocusedControl();

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                putOnClipboard();

                // The palette is still open while the command runs. It closes
                // right after the command returns. Wait for that before
                // typing, or the keystroke would go into its search box.
                if (ForegroundTracker.IsPaletteWindow(palette))
                {
                    var deadline = DateTime.UtcNow + CloseTimeout;
                    while (DateTime.UtcNow < deadline && GetForegroundWindow() == palette)
                    {
                        Thread.Sleep(25);
                    }

                    // Let the focus settle on the new window.
                    Thread.Sleep(120);
                }

                if (ForegroundTracker.IsPaletteWindow(GetForegroundWindow()))
                {
                    // The palette is still in front: typing now would go into
                    // its own search box.
                    ExtensionHost.LogMessage("notes paste: the palette was still in front, nothing was pasted");
                    return;
                }

                // The paste goes to the box with the caret in it. When the
                // palette closed, the focus may have landed somewhere else
                // entirely, so it is put back where it was.
                if (app != IntPtr.Zero && IsWindow(app))
                {
                    FocusControl(app, control);
                }

                SendControlV();
            }
            catch (Exception ex)
            {
                ExtensionHost.LogMessage("notes paste failed: " + ex.Message);
            }
        });
    }

    /// <summary>
    /// Gives the keyboard focus back to the app the user is working in, and to
    /// the control with the caret in it. The focus is per thread, so the
    /// threads are tied together while it is moved.
    /// </summary>
    private static void FocusControl(IntPtr app, IntPtr control)
    {
        var appThread = GetWindowThreadProcessId(app, out _);
        if (appThread == 0)
        {
            return;
        }

        var currentThread = GetCurrentThreadId();

        var foreground = GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);

        // Tying the threads together lets the focus be moved into a window of
        // another process, and lets that window be brought to the front even
        // when the foreground lock says no.
        AttachThreadInput(currentThread, appThread, true);
        if (foregroundThread != 0 && foregroundThread != appThread)
        {
            AttachThreadInput(currentThread, foregroundThread, true);
        }

        try
        {
            SetForegroundWindow(app);

            // The caret goes back into the box it was in. Without this the
            // focus lands on whatever the window decides, which is not always
            // the text box the user was typing in.
            if (control != IntPtr.Zero && IsWindow(control))
            {
                SetFocus(control);
            }
        }
        finally
        {
            if (foregroundThread != 0 && foregroundThread != appThread)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }

            AttachThreadInput(currentThread, appThread, false);
        }

        // Give the window a moment to come to the front and take the focus.
        Thread.Sleep(120);
    }

    /// <summary>
    /// Sends Ctrl+V as real keyboard input, so it reaches the focused control
    /// of whatever app is in front.
    /// </summary>
    private static void SendControlV()
    {
        var inputs = new Input[4];
        inputs[0] = Key(VirtualKeyControl, 0);
        inputs[1] = Key(VirtualKeyV, 0);
        inputs[2] = Key(VirtualKeyV, KeyEventKeyUp);
        inputs[3] = Key(VirtualKeyControl, KeyEventKeyUp);

        var sent = SendInput((uint)inputs.Length, ref inputs[0], Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            ExtensionHost.LogMessage("notes paste: only " + sent + " of 4 key events were sent");
        }
    }

    private static Input Key(ushort virtualKey, uint flags) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = virtualKey,
                ScanCode = 0,
                Flags = flags,
                Time = 0,
                ExtraInfo = IntPtr.Zero,
            },
        },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;

        // The mouse input is never used, but it is the largest member of the
        // native union: without it the struct would be smaller than the
        // INPUT the system expects and SendInput would refuse the keystrokes.
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, ref Input inputs, int size);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(IntPtr window);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint current, uint other, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SetFocus(IntPtr window);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
