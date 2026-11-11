using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace CLWallpaper.Ui
{
    abstract class Bindable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Raise([CallerMemberName] string name = null)
        {
            var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>A library folder in the sidebar.</summary>
    sealed class FolderVM : Bindable
    {
        public string Name { get; set; }
        public string Rel { get; set; }
        public int Depth { get; set; }
        public string Glyph { get; set; }
        public FolderVM Parent { get; set; }
        public readonly ObservableCollection<FolderVM> children = new ObservableCollection<FolderVM>();
        public ObservableCollection<FolderVM> Children { get { return children; } }
        public GridLength Indent { get { return new GridLength(Depth * 14); } }
        public Thickness IndentMargin { get { return new Thickness(Depth * 16, 0, 0, 0); } }

        int count;
        public int Count { get { return count; } set { count = value; Raise("CountText"); } }
        public string CountText { get { return count > 0 ? count.ToString() : ""; } }

        bool expanded, selected, drop;
        public bool IsExpanded { get { return expanded; } set { if (expanded != value) { expanded = value; Raise(); } } }
        public bool IsSelected { get { return selected; } set { if (selected != value) { selected = value; Raise(); } } }
        public bool IsDropTarget { get { return drop; } set { if (drop != value) { drop = value; Raise(); } } }
        public override string ToString() { return Name; }
    }

    /// <summary>A card in the gallery: sub-folder, video or image.</summary>
    sealed class EntryVM : Bindable
    {
        public string Name { get; set; }
        public string Rel { get; set; }
        public string FullPath { get; set; }
        public bool IsFolder { get; set; }
        public bool IsVideo { get; set; }
        public bool IsImage { get { return !IsFolder && !IsVideo; } }
        public bool IsGif { get; set; }
        public long Size { get; set; }
        public int ItemCount { get; set; }

        public string Glyph { get { return IsVideo ? "" : ""; } }
        public bool ShowGlyph { get { return !IsFolder && thumb == null; } }

        ImageSource thumb;
        public ImageSource Thumb
        {
            get { return thumb; }
            set { thumb = value; Raise(); Raise("HasThumb"); Raise("ShowGlyph"); }
        }
        public bool HasThumb { get { return thumb != null; } }

        double duration; int w, h;
        public double Duration { get { return duration; } }
        public int PixelW { get { return w; } }
        public int PixelH { get { return h; } }
        public void SetMeta(double dur, int width, int height)
        {
            duration = dur; w = width; h = height;
            Raise("Badge"); Raise("HasBadge"); Raise("Sub");
        }

        public string Badge
        {
            get
            {
                if (IsVideo) return duration > 0 ? Fmt.Clock(duration) : "Vidéo";
                if (IsGif) return "GIF";
                return "";
            }
        }
        public bool HasBadge { get { return !IsFolder && Badge.Length > 0; } }

        public string Sub
        {
            get
            {
                if (IsFolder) return ItemCount == 0 ? "Vide" : ItemCount + (ItemCount > 1 ? " éléments" : " élément");
                string kind = IsVideo ? "Vidéo" : (IsGif ? "GIF animé" : "Image");
                if (w > 0 && h > 0) return kind + " · " + Fmt.Res(w, h);
                return kind + " · " + Fmt.Size(Size);
            }
        }

        bool active, drop;
        public bool IsActive { get { return active; } set { if (active != value) { active = value; Raise(); } } }
        public bool IsDropTarget { get { return drop; } set { if (drop != value) { drop = value; Raise(); } } }

        public override string ToString() { return Name; }

        public static EntryVM ForFolder(string dir)
        {
            int n = 0;
            try { foreach (var f in Directory.EnumerateFileSystemEntries(dir)) if (Directory.Exists(f) || Store.IsMedia(f)) n++; } catch { }
            return new EntryVM { Name = Path.GetFileName(dir), FullPath = dir, Rel = Store.Rel(dir), IsFolder = true, ItemCount = n };
        }

        public static EntryVM ForFile(string file)
        {
            long size = 0;
            try { size = new FileInfo(file).Length; } catch { }
            return new EntryVM
            {
                Name = Path.GetFileNameWithoutExtension(file),
                FullPath = file,
                Rel = Store.Rel(file),
                IsVideo = Store.IsVideo(file),
                IsGif = string.Equals(Path.GetExtension(file), ".gif", System.StringComparison.OrdinalIgnoreCase),
                Size = size
            };
        }
    }

    static class Fmt
    {
        public static string Clock(double s)
        {
            if (s < 0) s = 0;
            int total = (int)System.Math.Round(s);
            if (total >= 3600) return (total / 3600) + ":" + ((total / 60) % 60).ToString("00") + ":" + (total % 60).ToString("00");
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        public static string Precise(double s)
        {
            if (s < 0) s = 0;
            int m = (int)(s / 60);
            double rest = s - m * 60;
            return m + ":" + rest.ToString("00.0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        }

        public static string Seconds(double s)
        {
            if (s < 60) return s.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " s";
            if (s < 3600) { int m = (int)(s / 60); int r = (int)System.Math.Round(s - m * 60); return r == 0 ? m + " min" : m + " min " + r + " s"; }
            return ((int)(s / 3600)) + " h";
        }

        public static string Size(long b)
        {
            var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            if (b >= 1L << 30) return (b / (double)(1L << 30)).ToString("0.0", fr) + " Go";
            if (b >= 1L << 20) return (b / (double)(1L << 20)).ToString("0.0", fr) + " Mo";
            return System.Math.Max(1, b / 1024) + " Ko";
        }

        public static string Res(int w, int h)
        {
            int m = System.Math.Min(w, h), M = System.Math.Max(w, h);
            if (M >= 3800) return "4K";
            if (M >= 2500) return "1440p";
            if (m >= 1070 || M >= 1900) return "1080p";
            if (m >= 700 || M >= 1270) return "720p";
            return w + "×" + h;
        }
    }
}
