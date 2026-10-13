using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ArcTV.App.Controls;

/// <summary>
/// A plain native window inside the WPF window, for mpv to draw its picture into (WPF cannot draw video itself). Whatever WPF draws in that spot is hidden behind it, so the
/// controls over the picture live in a separate transparent window.
/// </summary>
public sealed class MpvHost : HwndHost
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipChildren = 0x02000000;
    private const int SsBlackRect = 0x00000004; // draws plain black until the picture arrives

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr window);

    /// <summary>The native window mpv draws into (zero until the host is on screen).</summary>
    public IntPtr WindowHandle { get; private set; }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        WindowHandle = CreateWindowEx(0, "static", "", WsChild | WsVisible | WsClipChildren | SsBlackRect, 0, 0, 0, 0, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return new HandleRef(this, WindowHandle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DestroyWindow(hwnd.Handle);
        WindowHandle = IntPtr.Zero;
    }
}
