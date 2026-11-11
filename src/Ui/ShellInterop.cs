using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace CLWallpaper.Ui
{
    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
        void GetAttributes(uint mask, out uint attribs);
        void Compare(IShellItem psi, uint hint, out int order);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPERTYKEY { public Guid fmtid; public int pid; public PROPERTYKEY(string g, int p) { fmtid = new Guid(g); pid = p; } }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        void GetCount(out int c);
        void GetAt(int i, out PROPERTYKEY k);
        void GetValue(ref PROPERTYKEY key, IntPtr pv);
        void SetValue(ref PROPERTYKEY key, IntPtr pv);
        void Commit();
    }

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint c, IntPtr spec);
        void SetFileTypeIndex(uint i);
        void GetFileTypeIndex(out uint i);
        void Advise(IntPtr sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint fos);
        void GetOptions(out uint fos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem psi);
        void GetCurrentSelection(out IShellItem psi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem psi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string ext);
        void Close(int hr);
        void SetClientGuid(ref Guid g);
        void ClearClientData();
        void SetFilter(IntPtr filter);
        void GetResults(out IntPtr items);
        void GetSelectedItems(out IntPtr items);
    }

    static class Shell
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHGetPropertyStoreFromParsingName(string path, IntPtr pbc, int flags, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

        static readonly PROPERTYKEY PKEY_Duration = new PROPERTYKEY("64440490-4C8B-11D1-8B70-080036B11A03", 3);
        static readonly PROPERTYKEY PKEY_FrameW = new PROPERTYKEY("64440491-4C8B-11D1-8B70-080036B11A03", 3);
        static readonly PROPERTYKEY PKEY_FrameH = new PROPERTYKEY("64440491-4C8B-11D1-8B70-080036B11A03", 4);

        /// <summary>Video thumbnail from the Windows thumbnail cache (the same one Explorer uses).</summary>
        public static BitmapSource Thumbnail(string path, int size)
        {
            object o = null;
            IntPtr hbm = IntPtr.Zero;
            try
            {
                Guid iid = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out o);
                var f = (IShellItemImageFactory)o;
                if (f.GetImage(new SIZE(size, size), 0x8 | 0x1, out hbm) != 0 || hbm == IntPtr.Zero) return null;
                var bs = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze();
                return bs;
            }
            catch { return null; }
            finally
            {
                if (hbm != IntPtr.Zero) Native.DeleteObject(hbm);
                if (o != null) Marshal.ReleaseComObject(o);
            }
        }

        /// <summary>Duration (s) and frame size of a video, from the shell property system.</summary>
        public static void VideoMeta(string path, out double duration, out int w, out int h)
        {
            duration = 0; w = 0; h = 0;
            IPropertyStore ps = null;
            try
            {
                Guid iid = typeof(IPropertyStore).GUID;
                SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0, ref iid, out ps);
                long d = Read(ps, PKEY_Duration);
                if (d > 0) duration = d / 1e7;
                w = (int)Math.Max(0, Read(ps, PKEY_FrameW));
                h = (int)Math.Max(0, Read(ps, PKEY_FrameH));
            }
            catch { }
            finally { if (ps != null) Marshal.ReleaseComObject(ps); }
        }

        static long Read(IPropertyStore ps, PROPERTYKEY key)
        {
            IntPtr pv = PV.Alloc();
            try { var k = key; ps.GetValue(ref k, pv); return PV.ReadI8(pv); }
            catch { return -1; }
            finally { PV.Free(pv); }
        }

        /// <summary>Modern (Vista+) folder picker.</summary>
        public static string PickFolder(Window owner, string title, string initial)
        {
            IFileOpenDialog dlg = null;
            try
            {
                dlg = (IFileOpenDialog)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")));
                dlg.SetOptions(0x20 | 0x40 | 0x800); // PICKFOLDERS | FORCEFILESYSTEM | PATHMUSTEXIST
                dlg.SetTitle(title);
                if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial))
                {
                    object o;
                    Guid iid = typeof(IShellItem).GUID;
                    SHCreateItemFromParsingName(initial, IntPtr.Zero, ref iid, out o);
                    dlg.SetFolder((IShellItem)o);
                }
                var hwnd = owner != null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;
                if (dlg.Show(hwnd) != 0) return null;
                IShellItem item; dlg.GetResult(out item);
                string path; item.GetDisplayName(0x80058000, out path);
                return path;
            }
            catch { return null; }
            finally { if (dlg != null) Marshal.ReleaseComObject(dlg); }
        }

        public static void CreateShortcut(string lnkPath, string target, string args, string description)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object sh = Activator.CreateInstance(t);
            try
            {
                object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnkPath });
                Type lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
                lt.InvokeMember("Arguments", BindingFlags.SetProperty, null, lnk, new object[] { args ?? "" });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(target) });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target + ",0" });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { description });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                Marshal.ReleaseComObject(lnk);
            }
            finally { Marshal.ReleaseComObject(sh); }
        }

        public static void Recycle(string path)
        {
            if (Directory.Exists(path))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            else if (File.Exists(path))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }

        public static void Reveal(string path)
        {
            try
            {
                if (File.Exists(path)) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch { }
        }
    }

    static class Autostart
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "CL-Wallpaper";

        public static bool Enabled
        {
            get
            {
                try { using (var k = Registry.CurrentUser.OpenSubKey(Key)) return k != null && k.GetValue(Name) != null; }
                catch { return false; }
            }
        }

        public static void Set(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (on) k.SetValue(Name, "\"" + Store.ExePath + "\" --host" + Store.DataArg);
                else k.DeleteValue(Name, false);
            }
        }
    }
}
