// Generates assets\app.ico (16 → 256 px) for CL-Wallpaper. Built and run by build.ps1.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class MakeIcon
{
    static GraphicsPath Squircle(float x, float y, float s, float r)
    {
        var p = new GraphicsPath();
        float d = r * 2;
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + s - d, y, d, d, 270, 90);
        p.AddArc(x + s - d, y + s - d, d, d, 0, 90);
        p.AddArc(x, y + s - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            float m = size * 0.06f, s = size - 2 * m, r = s * 0.235f;
            using (var shape = Squircle(m, m, s, r))
            {
                using (var bg = new LinearGradientBrush(new PointF(m, m), new PointF(m + s, m + s), Color.FromArgb(0x4F, 0x46, 0xE5), Color.FromArgb(0xFF, 0x5E, 0x8A)))
                {
                    var blend = new ColorBlend(3);
                    blend.Colors = new[] { Color.FromArgb(0x3B, 0x4C, 0xF0), Color.FromArgb(0xA8, 0x55, 0xF7), Color.FromArgb(0xFF, 0x7A, 0x6B) };
                    blend.Positions = new[] { 0f, 0.55f, 1f };
                    bg.InterpolationColors = blend;
                    g.FillPath(bg, shape);
                }
                g.SetClip(shape);
                // sun
                float sun = s * 0.26f;
                using (var sb = new SolidBrush(Color.FromArgb(240, 255, 226, 140)))
                    g.FillEllipse(sb, m + s * 0.58f, m + s * 0.17f, sun, sun);
                // back wave
                using (var w1 = new GraphicsPath())
                {
                    w1.AddBezier(m, m + s * 0.66f, m + s * 0.30f, m + s * 0.48f, m + s * 0.62f, m + s * 0.80f, m + s, m + s * 0.58f);
                    w1.AddLine(m + s, m + s * 0.58f, m + s, m + s);
                    w1.AddLine(m + s, m + s, m, m + s);
                    w1.CloseFigure();
                    using (var b = new SolidBrush(Color.FromArgb(105, 255, 255, 255))) g.FillPath(b, w1);
                }
                // front wave
                using (var w2 = new GraphicsPath())
                {
                    w2.AddBezier(m, m + s * 0.80f, m + s * 0.34f, m + s * 0.64f, m + s * 0.66f, m + s * 0.94f, m + s, m + s * 0.74f);
                    w2.AddLine(m + s, m + s * 0.74f, m + s, m + s);
                    w2.AddLine(m + s, m + s, m, m + s);
                    w2.CloseFigure();
                    using (var b = new SolidBrush(Color.FromArgb(225, 255, 255, 255))) g.FillPath(b, w2);
                }
                g.ResetClip();
                if (size >= 32)
                    using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255), Math.Max(1f, size / 128f)))
                        g.DrawPath(pen, shape);
            }
        }
        return bmp;
    }

    static byte[] Dib(Bitmap b)
    {
        int w = b.Width, h = b.Height;
        int maskStride = ((w + 31) / 32) * 4;
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write((short)1); bw.Write((short)32);
        bw.Write(0); bw.Write(w * h * 4 + maskStride * h); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
        for (int y = h - 1; y >= 0; y--)
            for (int x = 0; x < w; x++)
            {
                var c = b.GetPixel(x, y);
                bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
            }
        bw.Write(new byte[maskStride * h]);
        return ms.ToArray();
    }

    static int Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "app.ico";
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
        var images = new List<byte[]>();
        foreach (int sz in sizes)
            using (var bmp = Render(sz))
            {
                if (sz >= 256) { var ms = new MemoryStream(); bmp.Save(ms, ImageFormat.Png); images.Add(ms.ToArray()); }
                else images.Add(Dib(bmp));
            }
        using (var fs = File.Create(outPath))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                bw.Write((byte)0); bw.Write((byte)0); bw.Write((short)1); bw.Write((short)32);
                bw.Write(images[i].Length); bw.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) bw.Write(img);
        }
        using (var preview = Render(256)) preview.Save(Path.ChangeExtension(outPath, ".png"), ImageFormat.Png);
        Console.WriteLine("Icon written: " + outPath);
        return 0;
    }
}
