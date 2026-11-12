// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace notes;

/// <summary>
/// Puts something on the clipboard and presses Ctrl+V in the window the user
/// came from, so a note can land in the text box (or canvas) they were working
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
    /// Copies the text to the clipboard and pastes it into the window the user
    /// came from.
    /// </summary>
    public static void PasteText(string text)
    {
        // The history keeps what the user copied, not what the app pasted, so
        // this copy is marked and skipped when the monitor sees it.
        ClipboardStore.Instance.MarkOwnCopy(text);

        ClipboardHelper.SetText(text);
        SendPaste();
    }

    /// <summary>
    /// Copies the picture to the clipboard and pastes it into the window the
    /// user came from.
    /// </summary>
    public static void PasteImage(string path)
    {
        ClipboardStore.Instance.MarkOwnCopy(path);

        ClipboardWriter.SetImage(path);
        SendPaste();
    }

    private static void SendPaste()
    {
        var palette = GetForegroundWindow();
        var target = ForegroundTracker.Instance.WindowBefore(palette);

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                // The palette is still open while the command runs. It closes
                // right after the command returns, and the focus goes back to
                // the app the user came from. Wait for that before typing.
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

                var foreground = GetForegroundWindow();
                if (ForegroundTracker.IsPaletteWindow(foreground))
                {
                    // The palette is still in front: typing now would go into
                    // its own search box.
                    return;
                }

                if (target != IntPtr.Zero && foreground != target && IsWindow(target))
                {
                    SetForegroundWindow(target);
                    Thread.Sleep(80);
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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(IntPtr window);
}
