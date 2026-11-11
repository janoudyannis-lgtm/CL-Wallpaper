using System;

namespace CLWallpaper
{
    /// <summary>
    /// Puts a window between the desktop wallpaper and the desktop icons.
    /// Handles both the classic layout (top-level WorkerW) and the
    /// Windows 11 24H2+ layout (WorkerW is a child of Progman, which has no redirection bitmap).
    /// </summary>
    public static class DesktopHost
    {
        public static IntPtr Progman, WorkerW, DefView, Parent;
        public static bool Raised;

        public static bool Prepare()
        {
            Progman = Native.FindWindow("Progman", null);
            if (Progman == IntPtr.Zero) return false;
            IntPtr r;
            Native.SendMessageTimeout(Progman, 0x052C, new IntPtr(0xD), new IntPtr(1), 0, 1000, out r);
            Native.SendMessageTimeout(Progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out r);

            DefView = Native.FindWindowEx(Progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            WorkerW = Native.FindWindowEx(Progman, IntPtr.Zero, "WorkerW", null);
            if (DefView != IntPtr.Zero && WorkerW != IntPtr.Zero)
            {
                Raised = true;
                Parent = Progman;
                return true;
            }

            // Classic layout: WorkerW that follows the top-level window hosting SHELLDLL_DefView.
            Raised = false;
            WorkerW = IntPtr.Zero;
            Native.EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                IntPtr dv = Native.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (dv != IntPtr.Zero)
                {
                    DefView = dv;
                    WorkerW = Native.FindWindowEx(IntPtr.Zero, h, "WorkerW", null);
                }
                return true;
            }, IntPtr.Zero);
            Parent = WorkerW != IntPtr.Zero ? WorkerW : Progman;
            return true;
        }

        public static RECT ToParent(RECT screen)
        {
            RECT r = screen;
            Native.MapWindowPoints(IntPtr.Zero, Parent, ref r, 2);
            return r;
        }

        public static void Attach(IntPtr hwnd, RECT screen)
        {
            if (Raised)
            {
                long ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
                Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_LAYERED);
                Native.SetLayeredWindowAttributes(hwnd, 0, 255, Native.LWA_ALPHA);
            }
            if (Native.GetAncestor(hwnd, 1) != Parent) Native.SetParent(hwnd, Parent);
            RECT r = ToParent(screen);
            IntPtr after = Raised ? DefView : IntPtr.Zero;
            Native.SetWindowPos(hwnd, after, r.Left, r.Top, r.Width, r.Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            // The Windows wallpaper layer goes to the very bottom: placing it right below *this* window would cover
            // the windows of the other screens (each new window is inserted just under the icons).
            if (Raised && WorkerW != IntPtr.Zero)
                Native.SetWindowPos(WorkerW, new IntPtr(1) /* HWND_BOTTOM */, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public static void Refresh()
        {
            if (Progman != IntPtr.Zero && Native.IsWindow(Progman))
                Native.RedrawWindow(Progman, IntPtr.Zero, IntPtr.Zero,
                    Native.RDW_INVALIDATE | Native.RDW_ERASE | Native.RDW_ALLCHILDREN | Native.RDW_UPDATENOW);
        }
    }
}
