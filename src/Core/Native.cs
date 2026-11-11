using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CLWallpaper
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    public static class Native
    {
        public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        public const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPSIBLINGS = 0x04000000, WS_CLIPCHILDREN = 0x02000000;
        public const int WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_TRANSPARENT = 0x20;
        public const uint LWA_ALPHA = 2;
        public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
        public const int WM_SIZE = 0x05, WM_PAINT = 0x0F, WM_ERASEBKGND = 0x14, WM_MOUSEACTIVATE = 0x21, WM_APP = 0x8000;
        public const uint RDW_INVALIDATE = 1, RDW_ERASE = 4, RDW_ALLCHILDREN = 0x80, RDW_UPDATENOW = 0x100;

        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")] public static extern bool SetThreadPriority(IntPtr thread, int priority);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr64(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong32(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr64(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong32(IntPtr h, int i, int v);
        public static long GetWindowLong(IntPtr h, int i) { return IntPtr.Size == 8 ? GetWindowLongPtr64(h, i).ToInt64() : GetWindowLong32(h, i); }
        public static void SetWindowLong(IntPtr h, int i, long v) { if (IntPtr.Size == 8) SetWindowLongPtr64(h, i, new IntPtr(v)); else SetWindowLong32(h, i, (int)v); }
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] public static extern int MapWindowPoints(IntPtr from, IntPtr to, ref RECT r, int n);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc f, IntPtr l);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string s);
        [DllImport("user32.dll")] public static extern bool RedrawWindow(IntPtr h, IntPtr rc, IntPtr rgn, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr BeginPaint(IntPtr h, out PAINTSTRUCT ps);
        [DllImport("user32.dll")] public static extern bool EndPaint(IntPtr h, ref PAINTSTRUCT ps);
        [DllImport("user32.dll")] public static extern int FillRect(IntPtr hdc, ref RECT r, IntPtr brush);
        [DllImport("gdi32.dll")] public static extern IntPtr GetStockObject(int i);
        [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr h, IntPtr r, bool erase);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateWindowEx(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int val, int size);
        [DllImport("mfplat.dll")] public static extern int MFStartup(int version, int flags);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] public static extern int StrCmpLogicalW(string a, string b);
        [DllImport("uxtheme.dll", EntryPoint = "#135")] public static extern int SetPreferredAppMode(int mode);
        [DllImport("uxtheme.dll", EntryPoint = "#136")] public static extern void FlushMenuThemes();

        public static string ClassOf(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, 256);
            return sb.ToString();
        }
    }
}
