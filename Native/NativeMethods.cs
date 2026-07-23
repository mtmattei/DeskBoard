using System;
using System.Runtime.InteropServices;

namespace DeskBoard.Native;

/// <summary>
/// Thin P/Invoke surface for the window styling and z-order calls the overlay needs.
/// Kept in one place so the window logic stays readable.
/// </summary>
internal static class NativeMethods
{
    // GetWindowLong / SetWindowLong index for the extended style bits.
    public const int GWL_EXSTYLE = -20;

    // Extended window styles.
    public const int WS_EX_TRANSPARENT = 0x0000_0020; // click-through (mouse passes to windows below)
    public const int WS_EX_TOOLWINDOW  = 0x0000_0080; // keep out of Alt-Tab
    public const int WS_EX_LAYERED     = 0x0008_0000; // per-pixel transparency (WPF sets this when AllowsTransparency=true)
    public const int WS_EX_NOACTIVATE  = 0x0800_0000; // do not steal focus when shown

    // SetWindowPos special hwnd targets.
    public static readonly IntPtr HWND_TOP    = new(0);
    public static readonly IntPtr HWND_BOTTOM = new(1);

    // SetWindowPos flags.
    public const uint SWP_NOSIZE      = 0x0001;
    public const uint SWP_NOMOVE      = 0x0002;
    public const uint SWP_NOACTIVATE  = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020; // force cached ex-style changes to apply
    public const uint SWP_SHOWWINDOW  = 0x0040;

    // RegisterHotKey modifiers and message.
    public const uint MOD_ALT      = 0x0001;
    public const uint MOD_CONTROL  = 0x0002;
    public const uint MOD_NOREPEAT = 0x4000;
    public const int  WM_HOTKEY    = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
