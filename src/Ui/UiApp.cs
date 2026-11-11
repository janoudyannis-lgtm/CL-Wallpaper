using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace CLWallpaper.Ui
{
    static class UiApp
    {
        public static Mutex InstanceMutex;

        public static int Run(string[] args)
        {
            bool configured = Store.Init(args);
            bool created;
            InstanceMutex = new Mutex(true, Ipc.UiMutex, out created);
            if (!created) { Ipc.SetEvent(Ipc.ShowEvent); return 0; }
            JustUpdated = Array.IndexOf(args, "--updated") >= 0;
            if (configured) Updater.Cleanup();
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (s, e) =>
            {
                Store.Log("ui: " + e.Exception);
                e.Handled = true;
            };
            var view = new MainView(configured);
            app.Run(view.Window);
            ReleaseInstance();
            return 0;
        }

        public static bool JustUpdated;

        public static void ReleaseInstance()
        {
            if (InstanceMutex == null) return;
            try { InstanceMutex.ReleaseMutex(); } catch { }
            InstanceMutex.Dispose();
            InstanceMutex = null;
        }
    }

    static class Res
    {
        public static object Xaml(string name)
        {
            using (var s = typeof(Res).Assembly.GetManifestResourceStream("CLWallpaper." + name))
            using (var r = new StreamReader(s, Encoding.UTF8))
                return XamlReader.Parse(r.ReadToEnd());
        }

        public static BitmapFrame Icon(int size)
        {
            using (var s = typeof(Res).Assembly.GetManifestResourceStream("CLWallpaper.app.ico"))
            {
                var dec = new IconBitmapDecoder(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                BitmapFrame best = dec.Frames[0];
                foreach (var f in dec.Frames)
                    if (Math.Abs(f.PixelWidth - size) < Math.Abs(best.PixelWidth - size)) best = f;
                return best;
            }
        }
    }

    /// <summary>
    /// The three interfaces (macOS, Base, Old School) × light/dark: palettes, fonts, radii,
    /// control templates (Modern.xaml / Retro.xaml) and window chrome (Mica, corners, dark title).
    /// Everything is swapped live through DynamicResource.
    /// </summary>
    static class Theme
    {
        public const int Mac = 0, Base = 1, Retro = 2;
        public static readonly string[] Names = { "macOS", "Base", "Old School" };
        public static bool IsDark { get; private set; }
        public static int Style { get; private set; }
        public static bool MicaSupported { get { return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22621; } }
        public static bool UsesMica { get { return Style == Mac && MicaSupported; } }

        static ResourceDictionary modernDict, retroDict;

        // key, mac light, mac dark, base light, base dark, retro light, retro dark
        static readonly string[,] palette = {
            { "Fg",             "#1D1D1F", "#F5F5F7", "#141413", "#F5F4EE", "#000000", "#E8E8E8" },
            { "Fg2",            "#6E6E73", "#A1A1A6", "#5E5D59", "#B8B5A9", "#202020", "#CFCFCF" },
            { "Fg3",            "#A1A1A6", "#6C6C70", "#9C9A92", "#7D7B73", "#808080", "#8C8C8C" },
            { "Accent",         "#007AFF", "#0A84FF", "#C6613F", "#D97757", "#000080", "#2A3B9C" },
            { "AccentFg",       "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF" },
            { "AccentHover",    "#0068DA", "#3D9BFF", "#AE5232", "#E28A6C", "#0000A8", "#3447B0" },
            { "AccentSoft",     "#1A007AFF", "#2E0A84FF", "#1FC6613F", "#33D97757", "#40000080", "#602A3B9C" },
            { "Success",        "#1E9E45", "#32D74B", "#4E7A33", "#8FBF6A", "#008000", "#5FBF5F" },
            { "SuccessSoft",    "#1F34C759", "#2632D74B", "#1F4E7A33", "#268FBF6A", "#00000000", "#00000000" },
            { "Danger",         "#FF3B30", "#FF453A", "#B53A2B", "#E5675A", "#A00000", "#FF6060" },
            { "ContentBg",      "#FFFFFF", "#1E1E20", "#FAF9F5", "#262624", "#C0C0C0", "#3C3C3C" },
            { "InspectorBg",    "#FAFAFC", "#232325", "#F5F4EE", "#2A2A27", "#C0C0C0", "#3C3C3C" },
            { "CardBg",         "#FFFFFF", "#2A2A2D", "#FFFFFF", "#30302E", "#C0C0C0", "#3C3C3C" },
            { "ThumbBg",        "#EEEEF2", "#2C2C2F", "#EDEBE3", "#353532", "#FFFFFF", "#1E1E1E" },
            { "HoverBg",        "#0F000000", "#17FFFFFF", "#10141413", "#14FAF9F5", "#00000000", "#00000000" },
            { "PressBg",        "#1C000000", "#24FFFFFF", "#1C141413", "#24FAF9F5", "#00000000", "#00000000" },
            { "HoverRing",      "#1F000000", "#2EFFFFFF", "#24141413", "#33FAF9F5", "#00000000", "#00000000" },
            { "SidebarSel",     "#1C000000", "#2BFFFFFF", "#E6E2D7", "#3A3936", "#000080", "#2A3B9C" },
            { "SidebarSelFg",   "#1D1D1F", "#F5F5F7", "#141413", "#F5F4EE", "#FFFFFF", "#FFFFFF" },
            { "Separator",      "#E2E2E7", "#38383B", "#E6E3DA", "#3B3A36", "#808080", "#1E1E1E" },
            { "ControlBg",      "#FFFFFF", "#3A3A3D", "#FFFFFF", "#353532", "#C0C0C0", "#3C3C3C" },
            { "ControlHover",   "#F4F4F6", "#444448", "#F6F5F0", "#3D3C39", "#C0C0C0", "#3C3C3C" },
            { "ControlBorder",  "#D1D1D6", "#4A4A4E", "#DAD7CC", "#4A4944", "#808080", "#1E1E1E" },
            { "SegBg",          "#E7E7EC", "#2C2C2F", "#EBE8DF", "#30302D", "#00000000", "#00000000" },
            { "SegSel",         "#FFFFFF", "#5A5A5F", "#FFFFFF", "#4A4944", "#C0C0C0", "#3C3C3C" },
            { "InputBg",        "#0E000000", "#1AFFFFFF", "#FFFFFF", "#30302E", "#FFFFFF", "#1E1E1E" },
            { "InputBorder",    "#00000000", "#00000000", "#DAD7CC", "#4A4944", "#00000000", "#00000000" },
            { "FieldBg",        "#FFFFFF", "#1C1C1E", "#FFFFFF", "#30302E", "#FFFFFF", "#1E1E1E" },
            { "TrackBg",        "#DADADF", "#4A4A4E", "#DDD9CE", "#4A4944", "#808080", "#1E1E1E" },
            { "KnobBg",         "#FFFFFF", "#E2E2E6", "#FFFFFF", "#EDEBE3", "#C0C0C0", "#3C3C3C" },
            { "SwitchOff",      "#E3E3E8", "#3E3E42", "#DDD9CE", "#4A4944", "#FFFFFF", "#1E1E1E" },
            { "ScrollThumb",    "#40000000", "#50FFFFFF", "#33141413", "#4DFAF9F5", "#C0C0C0", "#3C3C3C" },
            { "SheetBg",        "#F7F7F9", "#2A2A2D", "#FAF9F5", "#2C2C2A", "#C0C0C0", "#3C3C3C" },
            { "Dim",            "#2E000000", "#66000000", "#2E141413", "#80000000", "#00000000", "#00000000" },
            { "MenuBg",         "#F7F8F8FA", "#F72C2C2F", "#FCFBF8", "#30302E", "#C0C0C0", "#3C3C3C" },
            { "MenuBorder",     "#1F000000", "#26FFFFFF", "#1F141413", "#29FAF9F5", "#000000", "#000000" },
            { "MenuHighlight",  "#007AFF", "#0A84FF", "#EDEAE1", "#3D3C39", "#000080", "#2A3B9C" },
            { "MenuHighlightFg","#FFFFFF", "#FFFFFF", "#141413", "#F5F4EE", "#FFFFFF", "#FFFFFF" },
            { "ToastBg",        "#EB2C2C2E", "#EBF2F2F7", "#F2141413", "#F2FAF9F5", "#FFFFE1", "#4A4A30" },
            { "ToastFg",        "#FFFFFF", "#1D1D1F", "#FAF9F5", "#141413", "#000000", "#F0F0F0" },
            { "ToastBorder",    "#00000000", "#00000000", "#00000000", "#00000000", "#000000", "#000000" },
            { "Link",           "#007AFF", "#0A84FF", "#C6613F", "#D97757", "#0000FF", "#7FA8FF" },
            { "TreeIconFg",     "#007AFF", "#0A84FF", "#C6613F", "#D97757", "#B8860B", "#E0B840" },
            { "Face",           "#FFFFFF", "#1E1E20", "#FAF9F5", "#262624", "#C0C0C0", "#3C3C3C" },
            { "BevelLight",     "#FFFFFF", "#3A3A3D", "#FFFFFF", "#3B3A36", "#FFFFFF", "#6E6E6E" },
            { "BevelLight2",    "#E2E2E7", "#38383B", "#E6E3DA", "#3B3A36", "#DFDFDF", "#525252" },
            { "BevelShadow",    "#D1D1D6", "#4A4A4E", "#DAD7CC", "#4A4944", "#808080", "#1E1E1E" },
            { "BevelDark",      "#A1A1A6", "#6C6C70", "#9C9A92", "#7D7B73", "#000000", "#000000" },
            { "ListBg",         "#FFFFFF", "#1E1E20", "#FAF9F5", "#262624", "#FFFFFF", "#1E1E1E" },
            { "SelFg",          "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF" },
            { "TipBg",          "#F7F8F8FA", "#F72C2C2F", "#FCFBF8", "#30302E", "#FFFFE1", "#4A4A30" },
            { "TipFg",          "#1D1D1F", "#F5F5F7", "#141413", "#F5F4EE", "#000000", "#F0F0F0" },
            { "TitleFg",        "#1D1D1F", "#F5F5F7", "#141413", "#F5F4EE", "#FFFFFF", "#FFFFFF" },
            { "WindowBg",       "#ECECF0", "#28282B", "#F0EEE6", "#1F1E1D", "#C0C0C0", "#3C3C3C" },
            { "SidebarBg",      "#00000000", "#00000000", "#00000000", "#00000000", "#00000000", "#00000000" },
        };

        // key, mac, base, retro
        static readonly double[,] radii = {
            { 6, 8, 0 },    // RSmall
            { 7, 9, 0 },    // RButton
            { 11, 12, 0 },  // RCard
            { 10, 12, 0 },  // RThumb
            { 13, 15, 0 },  // RRing
            { 14, 16, 0 },  // RSheet
            { 9, 10, 0 },   // RMenu
            { 18, 18, 0 },  // RToast
            { 22, 22, 0 },  // RHero
        };
        static readonly string[] radiusKeys = { "RSmall", "RButton", "RCard", "RThumb", "RRing", "RSheet", "RMenu", "RToast", "RHero" };

        public static bool SystemDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        public static bool Resolve(int pref) { return pref == 2 || (pref == 0 && SystemDark()); }

        public static void Apply(ResourceDictionary r, int style, bool dark)
        {
            Style = Math.Max(0, Math.Min(2, style));
            IsDark = dark;
            int col = 1 + Style * 2 + (dark ? 1 : 0);
            for (int i = 0; i < palette.GetLength(0); i++) r[palette[i, 0]] = Brush(palette[i, col]);
            if (UsesMica)
            {
                r["WindowBg"] = Brush("#00000000");
                r["SidebarBg"] = Brush(dark ? "#1E000000" : "#59FFFFFF");
            }
            for (int i = 0; i < radiusKeys.Length; i++) r[radiusKeys[i]] = new CornerRadius(radii[i, Style]);

            // fonts
            r["IconFont"] = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
            if (Style == Retro)
            {
                r["UiFont"] = new FontFamily("Microsoft Sans Serif, Tahoma, Segoe UI");
                r["DisplayFont"] = new FontFamily("Microsoft Sans Serif, Tahoma, Segoe UI");
                r["UiFontSize"] = 12.0;
            }
            else
            {
                r["UiFont"] = new FontFamily("Segoe UI Variable Text, Segoe UI");
                r["DisplayFont"] = new FontFamily(Style == Base ? "Georgia, Cambria, Segoe UI" : "Segoe UI Variable Display, Segoe UI");
                r["UiFontSize"] = 13.0;
            }

            // special brushes
            var face = ToColor(palette[Index("Face"), col]);
            var light = ToColor(palette[Index("BevelLight"), col]);
            var shadow = ToColor(palette[Index("BevelShadow"), col]);
            r["Dither"] = Style == Retro ? Checker(face, light) : (Brush)Brush(palette[Index("ControlBg"), col]);
            r["EtchedH"] = Style == Retro ? HalfSplit(shadow, light) : (Brush)Brush(palette[Index("Separator"), col]);
            r["TitleActive"] = Gradient(dark ? "#000060" : "#000080", dark ? "#0A5A9C" : "#1084D0");
            r["TitleInactive"] = Gradient(dark ? "#404040" : "#808080", dark ? "#5C5C5C" : "#B5B5B5");

            // folder drawing
            if (Style == Mac) r["FolderArt"] = Folder("#2F8CEB", "#74C3FF", "#4AA6F7", null, false);
            else if (Style == Base) r["FolderArt"] = Folder(dark ? "#9A6A48" : "#C08A5B", dark ? "#D9B48A" : "#EBD2AE", dark ? "#C69A6C" : "#DDB78A", null, false);
            else r["FolderArt"] = Folder("#D6B04A", "#FFE88A", "#F4D060", "#000000", true);

            // control templates
            ResourceDictionary want;
            if (Style == Retro) want = retroDict ?? (retroDict = (ResourceDictionary)Res.Xaml("Retro.xaml"));
            else want = modernDict ?? (modernDict = (ResourceDictionary)Res.Xaml("Modern.xaml"));
            if (r.MergedDictionaries.Count == 0) r.MergedDictionaries.Add(want);
            else if (r.MergedDictionaries[0] != want) r.MergedDictionaries[0] = want;
        }

        static int Index(string key)
        {
            for (int i = 0; i < palette.GetLength(0); i++) if (palette[i, 0] == key) return i;
            return 0;
        }

        static Color ToColor(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }

        static SolidColorBrush Brush(string hex)
        {
            var b = new SolidColorBrush(ToColor(hex)); b.Freeze(); return b;
        }

        static Brush Gradient(string a, string b)
        {
            var g = new LinearGradientBrush(ToColor(a), ToColor(b), 0); g.Freeze(); return g;
        }

        static Brush HalfSplit(Color top, Color bottom)
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            g.GradientStops.Add(new GradientStop(top, 0)); g.GradientStops.Add(new GradientStop(top, 0.5));
            g.GradientStops.Add(new GradientStop(bottom, 0.5)); g.GradientStops.Add(new GradientStop(bottom, 1));
            g.Freeze(); return g;
        }

        /// <summary>Classic 1-pixel checkerboard used for pressed toggles and scrollbar tracks.</summary>
        static Brush Checker(Color a, Color b)
        {
            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(a), null, new RectangleGeometry(new Rect(0, 0, 2, 2))));
            var gg = new GeometryGroup();
            gg.Children.Add(new RectangleGeometry(new Rect(0, 0, 1, 1)));
            gg.Children.Add(new RectangleGeometry(new Rect(1, 1, 1, 1)));
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(b), null, gg));
            RenderOptions.SetEdgeMode(dg, EdgeMode.Aliased);
            var br = new DrawingBrush(dg) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 2, 2), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
            RenderOptions.SetCachingHint(br, CachingHint.Cache);
            br.Freeze();
            return br;
        }

        static DrawingImage Folder(string back, string front1, string front2, string outline, bool square)
        {
            var g = new DrawingGroup();
            Pen pen = outline == null ? null : new Pen(Brush(outline), 2.5);
            string backPath = square
                ? "M1,6 L34,6 L40,13 L95,13 L95,80 L1,80 Z"
                : "M0,9 C0,4 3,2 7,2 L31,2 C34,2 36,3 38,6 L41,10 L89,10 C93,10 96,13 96,17 L96,73 C96,77 93,80 89,80 L7,80 C3,80 0,77 0,73 Z";
            string frontPath = square
                ? "M1,22 L95,22 L95,80 L1,80 Z"
                : "M0,24 C0,20 3,17 7,17 L89,17 C93,17 96,20 96,24 L96,73 C96,77 93,80 89,80 L7,80 C3,80 0,77 0,73 Z";
            g.Children.Add(new GeometryDrawing(Brush(back), pen, Geometry.Parse(backPath)));
            var grad = new LinearGradientBrush(ToColor(front1), ToColor(front2), 90);
            g.Children.Add(new GeometryDrawing(grad, pen, Geometry.Parse(frontPath)));
            if (!square) g.Children.Add(new GeometryDrawing(Brush("#33FFFFFF"), null, Geometry.Parse("M7,18 L89,18 C92,18 95,20 95,23 L95,24 L1,24 L1,23 C1,20 4,18 7,18 Z")));
            else g.Children.Add(new GeometryDrawing(Brush("#80FFFFFF"), null, Geometry.Parse("M3,24 L93,24 L93,27 L3,27 Z")));
            var di = new DrawingImage(g);
            di.Freeze();
            return di;
        }

        public static void ApplyWindow(Window w)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            int v = IsDark ? 1 : 0;
            Native.DwmSetWindowAttribute(hwnd, 20, ref v, 4);
            int corner = Style == Retro ? 1 : 2; // DONOTROUND / ROUND
            Native.DwmSetWindowAttribute(hwnd, 33, ref corner, 4);
            if (MicaSupported)
            {
                int b = UsesMica ? 2 : 1; // MAINWINDOW (Mica) / NONE
                Native.DwmSetWindowAttribute(hwnd, 38, ref b, 4);
            }
            var src = HwndSource.FromHwnd(hwnd);
            if (src != null && src.CompositionTarget != null)
            {
                var bg = Application.Current.Resources["WindowBg"] as SolidColorBrush;
                src.CompositionTarget.BackgroundColor = UsesMica || bg == null ? Colors.Transparent : bg.Color;
            }
        }
    }
}
