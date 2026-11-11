using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace CLWallpaper.Host
{
    /// <summary>One borderless window per monitor, living between the wallpaper and the desktop icons.</summary>
    sealed class WallpaperWindow : NativeWindow, IDisposable
    {
        public readonly RECT Bounds;
        public VideoPlayer Video { get; private set; }
        ImageView image;
        bool paused, mute, layered;
        byte alpha = 255;
        public event Action Cycle;
        public event Action<string> Failed;

        /// <summary>Window opacity, used for fade transitions (0 shows the Windows wallpaper underneath).</summary>
        public void SetAlpha(byte a)
        {
            if (a == alpha && layered) return;
            alpha = a;
            if (!layered)
            {
                long ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
                if ((ex & Native.WS_EX_LAYERED) == 0) Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex | Native.WS_EX_LAYERED);
                layered = true;
            }
            Native.SetLayeredWindowAttributes(Handle, 0, a, Native.LWA_ALPHA);
        }

        void RaiseFailed(string msg) { var f = Failed; if (f != null) f(msg); }

        public WallpaperWindow(RECT screen)
        {
            Bounds = screen;
            RECT r = DesktopHost.ToParent(screen);
            var cp = new CreateParams();
            cp.Caption = "CL-Wallpaper";
            cp.Parent = DesktopHost.Parent;
            cp.Style = Native.WS_CHILD | Native.WS_CLIPSIBLINGS | Native.WS_CLIPCHILDREN;
            cp.ExStyle = Native.WS_EX_NOACTIVATE;
            cp.X = r.Left; cp.Y = r.Top; cp.Width = r.Width; cp.Height = r.Height;
            CreateHandle(cp);
            DesktopHost.Attach(Handle, screen);
        }

        public void ShowVideo(string path, ItemSettings s, bool muteAudio)
        {
            DisposeImage();
            mute = muteAudio;
            if (Video == null)
            {
                Video = VideoPlayer.Create(Handle);
                Video.CycleCompleted += delegate { var c = Cycle; if (c != null) c(); };
                Video.Failed += RaiseFailed;
            }
            Video.GlobalMute = muteAudio;
            Video.SetPaused(paused);
            try { Video.Open(path, s); }
            catch (Exception ex)
            {
                Store.Log("open " + path + ": " + ex.Message);
                RaiseFailed(File.Exists(path) ? VideoPlayer.Describe(ex.HResult) : VideoPlayer.Describe(unchecked((int)0x80070002)));
            }
        }

        public void ShowImage(string path, ItemSettings s)
        {
            DisposeVideo();
            DisposeImage();
            try { image = new ImageView(path, s.Fit, Handle); }
            catch (Exception ex)
            {
                Store.Log("image " + path + ": " + ex.Message);
                RaiseFailed(File.Exists(path) ? "Image illisible : format non pris en charge ou fichier endommagé." : "Fichier introuvable : il a peut-être été déplacé ou supprimé.");
                return;
            }
            image.SetPaused(paused);
            Native.InvalidateRect(Handle, IntPtr.Zero, false);
        }

        public void Clear()
        {
            DisposeVideo();
            DisposeImage();
            Native.InvalidateRect(Handle, IntPtr.Zero, false);
        }

        public void UpdateSettings(ItemSettings s, bool muteAudio)
        {
            if (Video != null && Video.FilePath != null)
            {
                Video.GlobalMute = muteAudio;
                Video.Update(s);
            }
            else if (image != null)
            {
                image.SetFit(s.Fit);
                Native.InvalidateRect(Handle, IntPtr.Zero, false);
            }
        }

        public void SetPaused(bool p)
        {
            if (paused == p) return;
            paused = p;
            if (Video != null) Video.SetPaused(p);
            if (image != null) image.SetPaused(p);
        }

        void DisposeVideo() { if (Video != null) { Video.Dispose(); Video = null; } }
        void DisposeImage() { if (image != null) { image.Dispose(); image = null; } }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case Native.WM_ERASEBKGND:
                    m.Result = (IntPtr)1;
                    return;
                case Native.WM_PAINT:
                    Paint();
                    m.Result = IntPtr.Zero;
                    return;
                case Native.WM_MOUSEACTIVATE:
                    m.Result = (IntPtr)3; // MA_NOACTIVATE
                    return;
                case Native.WM_SIZE:
                    if (Video != null) Video.ApplyFit();
                    Native.InvalidateRect(Handle, IntPtr.Zero, false);
                    break;
            }
            base.WndProc(ref m);
        }

        void Paint()
        {
            PAINTSTRUCT ps;
            IntPtr hdc = Native.BeginPaint(Handle, out ps);
            try
            {
                RECT rc; Native.GetClientRect(Handle, out rc);
                if (Video != null && Video.IsReady) Video.Repaint();
                else if (image != null) image.Paint(hdc, rc.Width, rc.Height);
                else Native.FillRect(hdc, ref rc, Native.GetStockObject(4)); // BLACK_BRUSH
            }
            catch (Exception ex) { Store.Log("paint: " + ex.Message); }
            finally { Native.EndPaint(Handle, ref ps); }
        }

        public void Dispose()
        {
            DisposeVideo();
            DisposeImage();
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }

    /// <summary>Static image (pre-scaled once) or animated GIF renderer using GDI+.</summary>
    sealed class ImageView : IDisposable
    {
        Image img;
        MemoryStream stream;
        Bitmap cache;
        int fit;
        readonly bool animated;
        bool animating, paused;
        readonly EventHandler onFrame;
        readonly IntPtr hwnd;
        BufferedGraphicsContext ctx;

        public ImageView(string path, int fit, IntPtr hwnd)
        {
            this.fit = fit;
            this.hwnd = hwnd;
            img = Load(path, out stream);
            animated = ImageAnimator.CanAnimate(img);
            onFrame = delegate { Native.InvalidateRect(hwnd, IntPtr.Zero, false); };
            if (animated) StartAnim();
        }

        static Image Load(string path, out MemoryStream ms)
        {
            ms = null;
            try
            {
                ms = new MemoryStream(File.ReadAllBytes(path));
                var i = Image.FromStream(ms, true, false);
                ApplyExifOrientation(i);
                return i;
            }
            catch
            {
                if (ms != null) { ms.Dispose(); ms = null; }
                return WicDecode.Load(path); // WebP, HEIC, AVIF… via Windows Imaging Component
            }
        }

        static void ApplyExifOrientation(Image i)
        {
            try
            {
                if (Array.IndexOf(i.PropertyIdList, 0x0112) < 0) return;
                int o = i.GetPropertyItem(0x0112).Value[0];
                RotateFlipType t;
                switch (o)
                {
                    case 2: t = RotateFlipType.RotateNoneFlipX; break;
                    case 3: t = RotateFlipType.Rotate180FlipNone; break;
                    case 4: t = RotateFlipType.Rotate180FlipX; break;
                    case 5: t = RotateFlipType.Rotate90FlipX; break;
                    case 6: t = RotateFlipType.Rotate90FlipNone; break;
                    case 7: t = RotateFlipType.Rotate270FlipX; break;
                    case 8: t = RotateFlipType.Rotate270FlipNone; break;
                    default: return;
                }
                if (!ImageAnimator.CanAnimate(i)) i.RotateFlip(t);
            }
            catch { }
        }

        void StartAnim() { if (!animating) { ImageAnimator.Animate(img, onFrame); animating = true; } }
        void StopAnim() { if (animating) { ImageAnimator.StopAnimate(img, onFrame); animating = false; } }

        public void SetPaused(bool p)
        {
            paused = p;
            if (!animated) return;
            if (p) StopAnim(); else StartAnim();
        }

        public void SetFit(int f)
        {
            if (f == fit) return;
            fit = f;
            if (cache != null) { cache.Dispose(); cache = null; }
        }

        public void Paint(IntPtr hdc, int w, int h)
        {
            if (w <= 0 || h <= 0 || img == null) return;
            if (animated)
            {
                if (ctx == null) ctx = new BufferedGraphicsContext();
                ctx.MaximumBuffer = new Size(w + 1, h + 1);
                using (var bg = ctx.Allocate(hdc, new Rectangle(0, 0, w, h)))
                {
                    if (!paused) ImageAnimator.UpdateFrames(img);
                    Draw(bg.Graphics, img, w, h, fit, false);
                    bg.Render();
                }
                return;
            }
            if (cache == null || cache.Width != w || cache.Height != h)
            {
                if (cache != null) cache.Dispose();
                cache = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(cache)) Draw(g, img, w, h, fit, true);
            }
            using (var g = Graphics.FromHdc(hdc))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(cache, 0, 0);
            }
        }

        static void Draw(Graphics g, Image src, int w, int h, int fit, bool hq)
        {
            g.Clear(Color.Black);
            g.InterpolationMode = hq ? InterpolationMode.HighQualityBicubic : InterpolationMode.Bilinear;
            g.PixelOffsetMode = hq ? PixelOffsetMode.HighQuality : PixelOffsetMode.Half;
            g.CompositingQuality = hq ? CompositingQuality.HighQuality : CompositingQuality.HighSpeed;
            int iw = src.Width, ih = src.Height;
            if (iw <= 0 || ih <= 0) return;
            using (var attr = new ImageAttributes())
            {
                attr.SetWrapMode(WrapMode.TileFlipXY); // no dark edge bleeding
                if (fit == 2)
                {
                    g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, iw, ih, GraphicsUnit.Pixel, attr);
                }
                else if (fit == 1)
                {
                    double sc = Math.Min((double)w / iw, (double)h / ih);
                    int dw = (int)Math.Round(iw * sc), dh = (int)Math.Round(ih * sc);
                    g.DrawImage(src, new Rectangle((w - dw) / 2, (h - dh) / 2, dw, dh), 0, 0, iw, ih, GraphicsUnit.Pixel, attr);
                }
                else
                {
                    double sc = Math.Max((double)w / iw, (double)h / ih);
                    double sw = w / sc, sh = h / sc;
                    g.DrawImage(src, new Rectangle(0, 0, w, h), (float)((iw - sw) / 2), (float)((ih - sh) / 2), (float)sw, (float)sh, GraphicsUnit.Pixel, attr);
                }
            }
        }

        public void Dispose()
        {
            StopAnim();
            if (cache != null) { cache.Dispose(); cache = null; }
            if (ctx != null) { ctx.Dispose(); ctx = null; }
            if (img != null) { img.Dispose(); img = null; }
            if (stream != null) { stream.Dispose(); stream = null; }
        }
    }
}
