using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CLWallpaper.Ui
{
    /// <summary>Generates and caches thumbnails + video metadata on a background thread.</summary>
    sealed class ThumbService
    {
        sealed class Job { public string Path; public int Gen; public Action<ImageSource, double, int, int> Done; }
        sealed class Meta { public double Dur; public int W, H; }

        readonly Dispatcher ui;
        readonly BlockingCollection<Job> queue = new BlockingCollection<Job>();
        readonly ConcurrentDictionary<string, ImageSource> memory = new ConcurrentDictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        readonly ConcurrentDictionary<string, Meta> metas = new ConcurrentDictionary<string, Meta>(StringComparer.OrdinalIgnoreCase);
        int generation;

        public ThumbService(Dispatcher ui)
        {
            this.ui = ui;
            var t = new Thread(Worker) { IsBackground = true, Name = "thumbs", Priority = ThreadPriority.BelowNormal };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        public void CancelPending() { Interlocked.Increment(ref generation); }

        public void Request(EntryVM e)
        {
            if (e.IsFolder) return;
            Request(e.FullPath, false, (img, d, w, h) =>
            {
                if (img != null) e.Thumb = img;
                e.SetMeta(d, w, h);
            });
        }

        /// <summary>Callback runs on the UI thread. "keep" = not cancelled by navigation.</summary>
        public void Request(string path, bool keep, Action<ImageSource, double, int, int> done)
        {
            string key = Key(path);
            ImageSource img; Meta m;
            if (key != null && memory.TryGetValue(key, out img) && metas.TryGetValue(key, out m)) { done(img, m.Dur, m.W, m.H); return; }
            queue.Add(new Job { Path = path, Gen = keep ? -1 : generation, Done = done });
        }

        public void Forget(string path)
        {
            string key = Key(path);
            if (key == null) return;
            ImageSource i; Meta m;
            memory.TryRemove(key, out i); metas.TryRemove(key, out m);
        }

        static string Key(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) return null;
                string s = path.ToLowerInvariant() + "|" + fi.LastWriteTimeUtc.Ticks + "|" + fi.Length;
                ulong h = 14695981039346656037UL;
                foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
                return h.ToString("x16");
            }
            catch { return null; }
        }

        void Worker()
        {
            foreach (var job in queue.GetConsumingEnumerable())
            {
                if (job.Gen != -1 && job.Gen != generation) continue;
                try
                {
                    string key = Key(job.Path);
                    if (key == null) continue;
                    ImageSource img;
                    Meta meta;
                    if (!metas.TryGetValue(key, out meta))
                    {
                        meta = new Meta();
                        if (Store.IsVideo(job.Path)) Shell.VideoMeta(job.Path, out meta.Dur, out meta.W, out meta.H);
                        else ImageSize(job.Path, out meta.W, out meta.H);
                        metas[key] = meta;
                    }
                    if (!memory.TryGetValue(key, out img))
                    {
                        img = Produce(job.Path, key);
                        if (img != null) memory[key] = img;
                    }
                    var j = job; var im = img; var mt = meta;
                    ui.BeginInvoke(new Action(() => j.Done(im, mt.Dur, mt.W, mt.H)), DispatcherPriority.Background);
                }
                catch (Exception ex) { Store.Log("thumb " + job.Path + ": " + ex.Message); }
            }
        }

        static ImageSource Produce(string path, string key)
        {
            string cache = Path.Combine(Store.ThumbDir, key + ".jpg");
            if (File.Exists(cache))
            {
                try { return LoadFile(cache, 0); } catch { }
            }
            BitmapSource bmp = Store.IsVideo(path) ? Shell.Thumbnail(path, 400) : DecodeImage(path, 400);
            if (bmp == null) return null;
            try
            {
                Directory.CreateDirectory(Store.ThumbDir);
                var enc = new JpegBitmapEncoder { QualityLevel = 86 };
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = File.Create(cache)) enc.Save(fs);
            }
            catch { }
            return bmp;
        }

        public static BitmapSource LoadFile(string path, int decodeWidth)
        {
            var bi = new BitmapImage();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bi.BeginInit();
                bi.StreamSource = fs;
                if (decodeWidth > 0) bi.DecodePixelWidth = decodeWidth;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.EndInit();
            }
            bi.Freeze();
            return bi;
        }

        static BitmapSource DecodeImage(string path, int width)
        {
            try { return LoadFile(path, width); }
            catch { return Shell.Thumbnail(path, width); }
        }

        static void ImageSize(string path, out int w, out int h)
        {
            w = 0; h = 0;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                    w = dec.Frames[0].PixelWidth; h = dec.Frames[0].PixelHeight;
                }
            }
            catch { }
        }
    }
}
