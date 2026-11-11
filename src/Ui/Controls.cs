using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;

namespace CLWallpaper.Ui
{
    /// <summary>macOS-style segmented control.</summary>
    sealed class Segmented : Border
    {
        readonly UniformGrid grid = new UniformGrid { Rows = 1 };
        readonly List<RadioButton> buttons = new List<RadioButton>();
        bool silent;
        public event Action<int> Changed;

        public Segmented(params string[] labels)
        {
            SetResourceReference(StyleProperty, "SegHost");
            Child = grid;
            string group = "seg" + Guid.NewGuid().ToString("N");
            for (int i = 0; i < labels.Length; i++)
            {
                var rb = new RadioButton { Content = labels[i], GroupName = group };
                rb.SetResourceReference(StyleProperty, "Seg");
                int idx = i;
                rb.Checked += delegate { if (!silent) { var c = Changed; if (c != null) c(idx); } };
                buttons.Add(rb);
                grid.Children.Add(rb);
            }
        }

        public int Selected
        {
            get { for (int i = 0; i < buttons.Count; i++) if (buttons[i].IsChecked == true) return i; return -1; }
            set
            {
                silent = true;
                if (value >= 0 && value < buttons.Count) buttons[value].IsChecked = true;
                else foreach (var b in buttons) b.IsChecked = false;
                silent = false;
            }
        }
    }

    /// <summary>Two-handle slider used to pick the part of a video that loops.</summary>
    sealed class RangeSlider : Grid
    {
        readonly Canvas canvas = new Canvas { Background = System.Windows.Media.Brushes.Transparent };
        readonly Border rail = new Border(), fill = new Border();
        readonly Thumb lo = new Thumb(), hi = new Thumb();
        double K { get { return lo.ActualWidth > 0 ? lo.ActualWidth : 18; } }
        double KH { get { return lo.ActualHeight > 0 ? lo.ActualHeight : 18; } }
        public double Minimum, Maximum = 1, Lower, Upper = 1;
        public event Action<bool> Changed;   // true = drag finished
        public event Action<double> Scrub;   // value under the handle being dragged

        public RangeSlider()
        {
            Height = 26;
            rail.Height = 4; rail.CornerRadius = new CornerRadius(2);
            rail.SetResourceReference(Border.BackgroundProperty, "TrackBg");
            fill.Height = 4; fill.CornerRadius = new CornerRadius(2);
            fill.SetResourceReference(Border.BackgroundProperty, "Accent");
            lo.SetResourceReference(StyleProperty, "Knob");
            hi.SetResourceReference(StyleProperty, "Knob");
            canvas.Children.Add(rail); canvas.Children.Add(fill); canvas.Children.Add(lo); canvas.Children.Add(hi);
            Children.Add(canvas);
            SizeChanged += delegate { Layout(); };
            lo.SizeChanged += delegate { Layout(); };
            lo.DragDelta += delegate { Drag(true); };
            hi.DragDelta += delegate { Drag(false); };
            lo.DragCompleted += delegate { Fire(true); };
            hi.DragCompleted += delegate { Fire(true); };
            canvas.MouseLeftButtonDown += OnRailClick;
        }

        double Gap { get { return Math.Max(0.25, (Maximum - Minimum) * 0.02); } }

        public void Set(double min, double max, double lower, double upper)
        {
            Minimum = min; Maximum = Math.Max(min + 0.1, max);
            Lower = Math.Max(Minimum, Math.Min(lower, Maximum));
            Upper = Math.Max(Lower, Math.Min(upper <= 0 ? Maximum : upper, Maximum));
            Layout();
        }

        double ValueAt(double x)
        {
            double w = Math.Max(1, ActualWidth - K);
            double t = Math.Max(0, Math.Min(1, (x - K / 2) / w));
            return Minimum + t * (Maximum - Minimum);
        }

        void Drag(bool isLo)
        {
            double v = ValueAt(Mouse.GetPosition(canvas).X);
            if (isLo) Lower = Math.Max(Minimum, Math.Min(v, Upper - Gap));
            else Upper = Math.Min(Maximum, Math.Max(v, Lower + Gap));
            Layout();
            Fire(false);
            var s = Scrub; if (s != null) s(isLo ? Lower : Upper);
        }

        void OnRailClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is Thumb || IsInThumb(e.OriginalSource as DependencyObject)) return;
            double v = ValueAt(e.GetPosition(canvas).X);
            if (Math.Abs(v - Lower) < Math.Abs(v - Upper)) Lower = Math.Min(v, Upper - Gap);
            else Upper = Math.Max(v, Lower + Gap);
            Layout();
            Fire(true);
        }

        static bool IsInThumb(DependencyObject d)
        {
            while (d != null) { if (d is Thumb) return true; d = System.Windows.Media.VisualTreeHelper.GetParent(d); }
            return false;
        }

        void Fire(bool final) { var c = Changed; if (c != null) c(final); }

        void Layout()
        {
            double w = Math.Max(1, ActualWidth - K), h = ActualHeight > 0 ? ActualHeight : Height;
            double range = Maximum - Minimum;
            double a = range > 0 ? (Lower - Minimum) / range * w : 0;
            double b = range > 0 ? (Upper - Minimum) / range * w : w;
            rail.Width = w; Canvas.SetLeft(rail, K / 2); Canvas.SetTop(rail, (h - 4) / 2);
            fill.Width = Math.Max(0, b - a); Canvas.SetLeft(fill, K / 2 + a); Canvas.SetTop(fill, (h - 4) / 2);
            Canvas.SetLeft(lo, a); Canvas.SetTop(lo, (h - KH) / 2);
            Canvas.SetLeft(hi, b); Canvas.SetTop(hi, (h - KH) / 2);
        }
    }

    /// <summary>Native child window hosting a live Media Foundation preview inside the inspector.</summary>
    sealed class VideoHost : HwndHost
    {
        IntPtr hwnd;
        public VideoPlayer Player { get; private set; }

        protected override HandleRef BuildWindowCore(HandleRef parent)
        {
            hwnd = Native.CreateWindowEx(0, "Static", "", Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPSIBLINGS,
                0, 0, 16, 9, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            try { Player = VideoPlayer.Create(hwnd); Player.GlobalMute = true; }
            catch (Exception ex) { Store.Log("preview: " + ex.Message); Player = null; }
            return new HandleRef(this, hwnd);
        }

        protected override IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_ERASEBKGND)
            {
                RECT rc; Native.GetClientRect(h, out rc);
                Native.FillRect(wParam, ref rc, Native.GetStockObject(4));
                handled = true;
                return (IntPtr)1;
            }
            if (msg == Native.WM_PAINT && Player != null && Player.IsReady)
            {
                PAINTSTRUCT ps;
                Native.BeginPaint(h, out ps);
                Player.Repaint();
                Native.EndPaint(h, ref ps);
                handled = true;
                return IntPtr.Zero;
            }
            return base.WndProc(h, msg, wParam, lParam, ref handled);
        }

        /// <summary>Corner radius of the preview (follows the interface style).</summary>
        public static double Radius = 10;

        protected override void OnWindowPositionChanged(Rect rcBoundingBox)
        {
            base.OnWindowPositionChanged(rcBoundingBox);
            UpdateRegion();
            if (Player != null) Player.ApplyFit();
        }

        public void UpdateRegion()
        {
            if (hwnd == IntPtr.Zero) return;
            RECT rc; Native.GetClientRect(hwnd, out rc);
            if (Radius <= 0) { Native.SetWindowRgn(hwnd, IntPtr.Zero, true); return; }
            var ps = PresentationSource.FromVisual(this);
            double dpi = ps != null && ps.CompositionTarget != null ? ps.CompositionTarget.TransformToDevice.M11 : 1;
            int r = (int)Math.Round(Radius * 2 * dpi);
            IntPtr rgn = Native.CreateRoundRectRgn(0, 0, rc.Right + 1, rc.Bottom + 1, r, r);
            Native.SetWindowRgn(hwnd, rgn, true); // the window owns the region afterwards
        }

        protected override void DestroyWindowCore(HandleRef h)
        {
            if (Player != null) { Player.Dispose(); Player = null; }
            Native.DestroyWindow(h.Handle);
            hwnd = IntPtr.Zero;
        }
    }
}
