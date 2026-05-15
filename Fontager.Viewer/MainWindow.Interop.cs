using System;
using System.Runtime.InteropServices;

namespace Fontager.Viewer;

/// <summary>
/// Win32 P/Invoke declarations used by <see cref="MainWindow"/>. Split out
/// from <c>MainWindow.xaml.cs</c> so the C# code-behind is about UI flow
/// rather than Win32 plumbing.
///
/// <para>
/// What lives here, by usage:
/// </para>
/// <list type="bullet">
///   <item><description><c>AddFontResourceEx</c> / <c>RemoveFontResourceEx</c> /
///     <c>AddFontResource</c> / <c>RemoveFontResource</c> — GDI font table
///     management, used by both the preview activation path (FR_PRIVATE) and
///     the install path (session-wide).</description></item>
///   <item><description><c>SendMessageTimeout</c> + <c>WM_FONTCHANGE</c> —
///     broadcast after install so Explorer / Settings → Fonts refresh.</description></item>
///   <item><description><c>SetWindowLongPtr</c> / <c>CallWindowProc</c> +
///     <see cref="WndProcDelegate"/> + <c>WM_GETMINMAXINFO</c> — subclass
///     the window proc to enforce a minimum tracking size.</description></item>
///   <item><description><c>ChangeWindowMessageFilterEx</c> + <c>WM_DROPFILES</c> /
///     <c>WM_COPYDATA</c> / <c>WM_COPYGLOBALDATA</c> — UIPI fix so
///     drag-and-drop and the Win32 file picker keep working when Fontager
///     is launched elevated and Explorer is not.</description></item>
///   <item><description><c>LoadImage</c> + <c>WM_SETICON</c> — apply the
///     multi-resolution app icon to both the Alt+Tab thumbnail and the
///     taskbar entry.</description></item>
///   <item><description><c>SendMessage</c> + <c>GetDpiForWindow</c> — generic
///     window messaging helpers shared across the above features.</description></item>
/// </list>
/// </summary>
public sealed partial class MainWindow
{
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool RemoveFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResource(string lpFileName);

    /// <summary>Undoes <see cref="AddFontResource"/> (session-wide font table). May need multiple calls (ref-counted).</summary>
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RemoveFontResource(string lpFileName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    private const uint WM_FONTCHANGE = 0x001D;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message, uint action, IntPtr pChangeFilterStruct);

    private const uint MSGFLT_ALLOW = 1;
    private const uint WM_DROPFILES = 0x0233;
    private const uint WM_COPYDATA = 0x004A;
    private const uint WM_COPYGLOBALDATA = 0x0049;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const uint LR_DEFAULTSIZE = 0x00000040;
    private const uint LR_SHARED = 0x00008000;
    private const uint WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private WndProcDelegate? _wndProcDelegate;
    private IntPtr _oldWndProc;

    private const int GWL_WNDPROC = -4;
    private const uint WM_GETMINMAXINFO = 0x0024;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    /// <summary>
    /// GDI font-resource flag: register the font only inside this process so
    /// preview activation doesn't leak the font into the global font list.
    /// </summary>
    private const uint FR_PRIVATE = 0x10;
}
