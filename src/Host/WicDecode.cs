using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CLWallpaper.Host
{
    /// <summary>
    /// Fallback decoder for formats GDI+ can't read (WebP, HEIC, AVIF…), through WIC.
    /// Kept in its own class so WPF imaging is only loaded when actually needed.
    /// </summary>
    static class WicDecode
    {
        public static Bitmap Load(string path)
        {
            BitmapSource src;
            using (var fs = File.OpenRead(path))
            {
                var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                src = dec.Frames[0];
            }
            if (src.Format != PixelFormats.Pbgra32) src = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
            var bmp = new Bitmap(src.PixelWidth, src.PixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, bmp.PixelFormat);
            try { src.CopyPixels(System.Windows.Int32Rect.Empty, data.Scan0, data.Stride * data.Height, data.Stride); }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }
    }
}
