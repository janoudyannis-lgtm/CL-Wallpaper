using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace CLWallpaper
{
    /// <summary>Per-media settings (display, trim, speed, loop, sound).</summary>
    public sealed class ItemSettings
    {
        public int Fit;            // 0 = remplir, 1 = ajuster, 2 = étirer
        public double Start;       // secondes
        public double End;         // secondes (0 = fin de la vidéo)
        public double Speed = 1;
        public int Loop;           // 0 = en boucle, 1 = une fois, 2 = avec pause
        public double PauseSec = 3;
        public double Volume;      // 0..1
        public bool LoopFade;      // fondu enchaîné entre la fin et le début à chaque boucle

        public ItemSettings Clone() { return (ItemSettings)MemberwiseClone(); }
        public bool IsDefault
        {
            get { return Fit == 0 && Start <= 0 && End <= 0 && Math.Abs(Speed - 1) < 0.001 && Loop == 0 && Math.Abs(PauseSec - 3) < 0.001 && Volume <= 0 && !LoopFade; }
        }
        public Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object> {
                { "fit", Fit }, { "start", Start }, { "end", End }, { "speed", Speed },
                { "loop", Loop }, { "pause", PauseSec }, { "volume", Volume }, { "loopFade", LoopFade } };
        }
        public static ItemSettings From(IDictionary<string, object> d)
        {
            var s = new ItemSettings();
            if (d == null) return s;
            s.Fit = Json.I(d, "fit", 0); s.Start = Json.D(d, "start", 0); s.End = Json.D(d, "end", 0);
            s.Speed = Json.D(d, "speed", 1); s.Loop = Json.I(d, "loop", 0); s.PauseSec = Json.D(d, "pause", 3);
            s.Volume = Json.D(d, "volume", 0); s.LoopFade = Json.B(d, "loopFade", false);
            return s;
        }
    }

    /// <summary>Playlist settings of a library folder.</summary>
    public sealed class FolderSettings
    {
        public int Order;              // 0 = dans l'ordre, 1 = aléatoire
        public bool Recursive = true;
        public double ImageSec = 30;
        public int VideoLoops = 1;

        public FolderSettings Clone() { return (FolderSettings)MemberwiseClone(); }
        public bool IsDefault { get { return Order == 0 && Recursive && Math.Abs(ImageSec - 30) < 0.001 && VideoLoops == 1; } }
        public Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object> { { "order", Order }, { "recursive", Recursive }, { "imageSec", ImageSec }, { "videoLoops", VideoLoops } };
        }
        public static FolderSettings From(IDictionary<string, object> d)
        {
            var f = new FolderSettings();
            if (d == null) return f;
            f.Order = Json.I(d, "order", 0); f.Recursive = Json.B(d, "recursive", true);
            f.ImageSec = Json.D(d, "imageSec", 30); f.VideoLoops = Math.Max(1, Json.I(d, "videoLoops", 1));
            return f;
        }
    }

    public sealed class AppSettings
    {
        public int Theme;                 // 0 = système, 1 = clair, 2 = sombre
        public int Style;                 // 0 = macOS, 1 = Base, 2 = Old School
        public bool PauseFullscreen = true;
        public bool PauseMaximized = true;
        public bool PauseBattery = true;
        public int Monitors;              // 0 = même fond partout, 1 = écran principal, 2 = un fond par écran
        public bool Fade = true;          // fondus entre deux fonds
        public bool Mute;
        public double CardSize = 200;
        public bool ConvertGif = true;    // GIF animés convertis en MP4 à l'import
        public bool Schedule;             // fond de jour / fond de nuit
        public int DayStart = 7 * 60, NightStart = 20 * 60;   // minutes depuis minuit
        public PlayState Day = new PlayState(), Night = new PlayState();
        public bool UpdateCheck;          // vérifier les mises à jour au démarrage
        public const string DefaultUpdateSource = "github:janoudyannis-lgtm/CL-Wallpaper";
        public string UpdateSource = DefaultUpdateSource;  // URL d'un manifeste JSON, ou « github:propriétaire/dépôt »

        /// <summary>Night when the clock is outside [DayStart, NightStart) (handles a night start before the day start).</summary>
        public bool IsNight(DateTime now)
        {
            int m = now.Hour * 60 + now.Minute;
            return DayStart <= NightStart ? (m < DayStart || m >= NightStart) : (m >= NightStart && m < DayStart);
        }
        /// <summary>Last time the schedule switched (day or night start), before <paramref name="now"/>.</summary>
        public DateTime LastSwitch(DateTime now)
        {
            DateTime best = DateTime.MinValue;
            foreach (int min in new[] { DayStart, NightStart })
                for (int d = -1; d <= 0; d++)
                {
                    var t = now.Date.AddDays(d).AddMinutes(min);
                    if (t <= now && t > best) best = t;
                }
            return best;
        }
        public bool ScheduleActive { get { return Schedule && (Day.IsSet || Night.IsSet); } }

        public Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object> {
                { "theme", Theme }, { "style", Style }, { "pauseFullscreen", PauseFullscreen }, { "pauseMaximized", PauseMaximized },
                { "pauseBattery", PauseBattery }, { "monitors", Monitors }, { "fade", Fade }, { "mute", Mute }, { "cardSize", CardSize },
                { "convertGif", ConvertGif }, { "schedule", Schedule }, { "dayStart", DayStart }, { "nightStart", NightStart },
                { "day", Day.ToDict() }, { "night", Night.ToDict() }, { "updateCheck", UpdateCheck }, { "updateSource", UpdateSource } };
        }
        public static AppSettings From(IDictionary<string, object> d)
        {
            var a = new AppSettings();
            if (d == null) return a;
            a.Theme = Json.I(d, "theme", 0); a.Style = Math.Max(0, Math.Min(2, Json.I(d, "style", 0))); a.PauseFullscreen = Json.B(d, "pauseFullscreen", true);
            a.PauseMaximized = Json.B(d, "pauseMaximized", true); a.PauseBattery = Json.B(d, "pauseBattery", true);
            a.Monitors = Json.I(d, "monitors", 0); a.Fade = Json.B(d, "fade", true); a.Mute = Json.B(d, "mute", false); a.CardSize = Json.D(d, "cardSize", 200);
            a.ConvertGif = Json.B(d, "convertGif", true); a.Schedule = Json.B(d, "schedule", false);
            a.DayStart = Json.I(d, "dayStart", 420); a.NightStart = Json.I(d, "nightStart", 1200);
            a.Day = PlayState.From(Json.O(d, "day")); a.Night = PlayState.From(Json.O(d, "night"));
            a.UpdateCheck = Json.B(d, "updateCheck", false); a.UpdateSource = Json.S(d, "updateSource", DefaultUpdateSource);
            return a;
        }
    }

    /// <summary>What the wallpaper host should display.</summary>
    public sealed class PlayState
    {
        public string Kind = "none";   // none | item | folder
        public string Path = "";
        public bool Paused;
        /// <summary>When the user last chose this wallpaper (UTC ticks): a manual choice wins over the day/night schedule until its next switch.</summary>
        public long At;
        /// <summary>Per-screen overrides (index = Screen.AllScreens order), used in "un fond par écran" mode.</summary>
        public List<PlayState> Screens = new List<PlayState>();

        public bool IsSet { get { return (Kind == "item" && Path.Length > 0) || Kind == "folder"; } }
        public bool Same(PlayState o) { return o != null && Kind == o.Kind && string.Equals(Path, o.Path, StringComparison.OrdinalIgnoreCase); }

        public Dictionary<string, object> ToDict()
        {
            var d = new Dictionary<string, object> { { "kind", Kind }, { "path", Path }, { "paused", Paused }, { "at", (double)At } };
            if (Screens.Count > 0)
                d["screens"] = Screens.Select(s => (object)new Dictionary<string, object> { { "kind", s.Kind }, { "path", s.Path } }).ToList();
            return d;
        }
        public static PlayState From(IDictionary<string, object> d)
        {
            var p = new PlayState();
            if (d == null) return p;
            p.Kind = Json.S(d, "kind", "none"); p.Path = Json.S(d, "path", ""); p.Paused = Json.B(d, "paused", false); p.At = (long)Json.D(d, "at", 0);
            object sc;
            if (d.TryGetValue("screens", out sc) && sc is List<object>)
                foreach (var o in (List<object>)sc) p.Screens.Add(From(o as IDictionary<string, object>));
            return p;
        }

        /// <summary>What screen <paramref name="i"/> should show in per-screen mode (falls back to the main choice).</summary>
        public PlayState ForScreen(int i)
        {
            if (i >= 0 && i < Screens.Count && Screens[i].IsSet) return Screens[i];
            return this;
        }
    }

    public sealed class LibraryData
    {
        public readonly Dictionary<string, ItemSettings> Items = new Dictionary<string, ItemSettings>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, FolderSettings> Folders = new Dictionary<string, FolderSettings>(StringComparer.OrdinalIgnoreCase);

        public ItemSettings Item(string rel) { ItemSettings s; return Items.TryGetValue(rel ?? "", out s) ? s.Clone() : new ItemSettings(); }
        public FolderSettings Folder(string rel) { FolderSettings s; return Folders.TryGetValue(rel ?? "", out s) ? s.Clone() : new FolderSettings(); }
        public void SetItem(string rel, ItemSettings s) { if (s.IsDefault) Items.Remove(rel); else Items[rel] = s.Clone(); }
        public void SetFolder(string rel, FolderSettings s) { if (s.IsDefault) Folders.Remove(rel); else Folders[rel] = s.Clone(); }

        public void Rename(string oldRel, string newRel)
        {
            RenameIn(Items, oldRel, newRel);
            RenameIn(Folders, oldRel, newRel);
        }
        static void RenameIn<T>(Dictionary<string, T> d, string oldRel, string newRel)
        {
            foreach (var k in d.Keys.ToList())
            {
                string nk = null;
                if (string.Equals(k, oldRel, StringComparison.OrdinalIgnoreCase)) nk = newRel;
                else if (k.StartsWith(oldRel + "\\", StringComparison.OrdinalIgnoreCase)) nk = newRel + k.Substring(oldRel.Length);
                if (nk != null) { T v = d[k]; d.Remove(k); d[nk] = v; }
            }
        }
        public void Remove(string rel)
        {
            foreach (var k in Items.Keys.ToList()) if (Store.IsSameOrChild(k, rel)) Items.Remove(k);
            foreach (var k in Folders.Keys.ToList()) if (Store.IsSameOrChild(k, rel)) Folders.Remove(k);
        }

        public Dictionary<string, object> ToDict()
        {
            var items = new Dictionary<string, object>();
            foreach (var kv in Items) items[kv.Key] = kv.Value.ToDict();
            var folders = new Dictionary<string, object>();
            foreach (var kv in Folders) folders[kv.Key] = kv.Value.ToDict();
            return new Dictionary<string, object> { { "items", items }, { "folders", folders } };
        }
        public static LibraryData From(IDictionary<string, object> d)
        {
            var l = new LibraryData();
            var items = Json.O(d, "items");
            if (items != null) foreach (var kv in items) l.Items[kv.Key] = ItemSettings.From(kv.Value as IDictionary<string, object>);
            var folders = Json.O(d, "folders");
            if (folders != null) foreach (var kv in folders) l.Folders[kv.Key] = FolderSettings.From(kv.Value as IDictionary<string, object>);
            return l;
        }
    }

    /// <summary>Paths, persistence and media helpers shared by the UI and the wallpaper host.</summary>
    public static class Store
    {
        public const string AppName = "CL-Wallpaper";
        public const string Version = "1.2.3";
        public const string LibraryName = "Bibliothèque";
        public static readonly string[] VideoExt = { ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".mpg", ".mpeg", ".ts", ".m2ts", ".3gp" };
        public static readonly string[] ImageExt = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".jfif", ".heic", ".avif" };

        public static string DataDir, MetaDir, LibraryDir, ThumbDir;
        public static bool DataForced;
        static readonly object logLock = new object();

        public static string ExePath { get { return Process.GetCurrentProcess().MainModule.FileName; } }
        public static string ExeDir { get { return Path.GetDirectoryName(ExePath); } }
        public static string DataArg { get { return DataForced ? " --data \"" + DataDir + "\"" : ""; } }
        public static string PointerFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName, "location.txt"); }
        }
        public static bool IsInitialized { get { return DataDir != null && Directory.Exists(MetaDir) && Directory.Exists(LibraryDir); } }

        /// <summary>Finds the app directory. Returns false when the first-run setup is needed.</summary>
        public static bool Init(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--data") { DataForced = true; SetDir(args[i + 1]); return IsInitialized; }

            if (Directory.Exists(Path.Combine(ExeDir, ".clwallpaper")) && Directory.Exists(Path.Combine(ExeDir, LibraryName)))
            { SetDir(ExeDir); return true; }
            try
            {
                if (File.Exists(PointerFile))
                {
                    string d = File.ReadAllText(PointerFile, Encoding.UTF8).Trim();
                    if (d.Length > 0) { SetDir(d); if (IsInitialized) return true; }
                }
            }
            catch { }
            DataDir = null;
            return false;
        }

        public static string SuggestedDir
        {
            get { return DataDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), AppName); }
        }

        public static void SetDir(string dir)
        {
            DataDir = Path.GetFullPath(dir);
            MetaDir = Path.Combine(DataDir, ".clwallpaper");
            LibraryDir = Path.Combine(DataDir, LibraryName);
            ThumbDir = Path.Combine(MetaDir, "thumbs");
        }

        public static void CreateStructure()
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(LibraryDir);
            var di = Directory.CreateDirectory(MetaDir);
            try { di.Attributes |= FileAttributes.Hidden; } catch { }
            Directory.CreateDirectory(ThumbDir);
        }

        public static void WritePointer(string dir)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PointerFile));
            File.WriteAllText(PointerFile, dir, Encoding.UTF8);
        }

        // ---------- media helpers ----------
        public static bool IsVideo(string p) { return VideoExt.Contains((Path.GetExtension(p) ?? "").ToLowerInvariant()); }
        public static bool IsImage(string p) { return ImageExt.Contains((Path.GetExtension(p) ?? "").ToLowerInvariant()); }
        public static bool IsMedia(string p) { return IsVideo(p) || IsImage(p); }

        public static string Full(string rel) { return string.IsNullOrEmpty(rel) ? LibraryDir : Path.Combine(LibraryDir, rel); }
        public static string Rel(string full)
        {
            string f = Path.GetFullPath(full).TrimEnd('\\');
            string root = LibraryDir.TrimEnd('\\');
            if (string.Equals(f, root, StringComparison.OrdinalIgnoreCase)) return "";
            if (f.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) return f.Substring(root.Length + 1);
            return f;
        }
        public static bool IsSameOrChild(string rel, string parent)
        {
            if (string.IsNullOrEmpty(parent)) return true;
            return string.Equals(rel, parent, StringComparison.OrdinalIgnoreCase) || rel.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase);
        }

        public static List<string> SortedDirs(string dir)
        {
            var l = new List<string>();
            try { l.AddRange(Directory.GetDirectories(dir).Where(d => (new DirectoryInfo(d).Attributes & FileAttributes.Hidden) == 0)); } catch { }
            l.Sort((a, b) => Native.StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)));
            return l;
        }
        public static List<string> SortedMedia(string dir, bool recursive)
        {
            var l = new List<string>();
            try
            {
                var files = Directory.GetFiles(dir).Where(IsMedia).ToList();
                files.Sort((a, b) => Native.StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)));
                l.AddRange(files);
                if (recursive) foreach (var d in SortedDirs(dir)) l.AddRange(SortedMedia(d, true));
            }
            catch { }
            return l;
        }

        // ---------- persistence ----------
        static string MetaFile(string name) { return Path.Combine(MetaDir, name); }

        static IDictionary<string, object> ReadJson(string name)
        {
            string f = MetaFile(name);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    if (!File.Exists(f)) return null;
                    return Json.Parse(File.ReadAllText(f, Encoding.UTF8)) as IDictionary<string, object>;
                }
                catch (IOException) { Thread.Sleep(40); }
                catch { return null; }
            }
            return null;
        }

        static void WriteJson(string name, object o)
        {
            string f = MetaFile(name);
            string tmp = f + "." + Process.GetCurrentProcess().Id + ".tmp";
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    File.WriteAllText(tmp, Json.Write(o), new UTF8Encoding(false));
                    if (File.Exists(f)) File.Replace(tmp, f, null); else File.Move(tmp, f);
                    return;
                }
                catch (IOException) { Thread.Sleep(50); }
                catch (UnauthorizedAccessException) { Thread.Sleep(50); }
            }
            try { File.Copy(tmp, f, true); File.Delete(tmp); } catch (Exception ex) { Log("write " + name + ": " + ex.Message); }
        }

        public static AppSettings LoadSettings() { return AppSettings.From(ReadJson("settings.json")); }
        public static void SaveSettings(AppSettings s) { WriteJson("settings.json", s.ToDict()); }
        public static LibraryData LoadLibrary() { return LibraryData.From(ReadJson("library.json")); }
        public static void SaveLibrary(LibraryData l) { WriteJson("library.json", l.ToDict()); }
        public static PlayState LoadState() { return PlayState.From(ReadJson("state.json")); }
        public static void SaveState(PlayState s) { WriteJson("state.json", s.ToDict()); }
        public static IDictionary<string, object> LoadStatus() { return ReadJson("status.json"); }
        public static void SaveStatus(Dictionary<string, object> d) { WriteJson("status.json", d); }
        public static DateTime StatusTime { get { try { return File.GetLastWriteTimeUtc(MetaFile("status.json")); } catch { return DateTime.MinValue; } } }

        public static void Log(string msg)
        {
            try
            {
                string dir = MetaDir != null && Directory.Exists(MetaDir) ? MetaDir : Path.GetTempPath();
                string f = Path.Combine(dir, "log.txt");
                lock (logLock)
                {
                    if (File.Exists(f) && new FileInfo(f).Length > 512 * 1024) File.Delete(f);
                    File.AppendAllText(f, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }

    /// <summary>Named kernel objects used to talk between the window (UI) and the wallpaper host.</summary>
    public static class Ipc
    {
        public const string HostMutex = "Local\\CLWallpaper.Host.v1";
        public const string ReloadEvent = "Local\\CLWallpaper.Reload.v1";
        public const string QuitEvent = "Local\\CLWallpaper.Quit.v1";
        // One window per data directory when "--data" is used (portable/test copies), otherwise one per session.
        public static string UiMutex { get { return "Local\\CLWallpaper.UI.v1" + Suffix; } }
        public static string ShowEvent { get { return "Local\\CLWallpaper.Show.v1" + Suffix; } }

        static string Suffix
        {
            get
            {
                if (!Store.DataForced || Store.DataDir == null) return "";
                ulong h = 14695981039346656037UL;
                foreach (char c in Store.DataDir.ToLowerInvariant()) { h ^= c; h *= 1099511628211UL; }
                return "." + h.ToString("x16");
            }
        }

        public static bool Exists(string mutexName)
        {
            Mutex m;
            if (Mutex.TryOpenExisting(mutexName, out m)) { m.Dispose(); return true; }
            return false;
        }

        public static bool SetEvent(string name)
        {
            EventWaitHandle e;
            if (EventWaitHandle.TryOpenExisting(name, out e)) { e.Set(); e.Dispose(); return true; }
            return false;
        }

        public static bool HostRunning { get { return Exists(HostMutex); } }

        public static void StartHost()
        {
            try { Process.Start(new ProcessStartInfo(Store.ExePath, "--host" + Store.DataArg) { UseShellExecute = false }); }
            catch (Exception ex) { Store.Log("start host: " + ex.Message); }
        }

        /// <summary>Asks the host to re-read its files; starts it when needed.</summary>
        public static void NotifyHost(bool startIfNeeded)
        {
            if (HostRunning) SetEvent(ReloadEvent);
            else if (startIfNeeded) StartHost();
        }

        public static void StartUi()
        {
            if (Exists(UiMutex)) { SetEvent(ShowEvent); return; }
            try { Process.Start(new ProcessStartInfo(Store.ExePath, Store.DataArg.Trim()) { UseShellExecute = false }); }
            catch (Exception ex) { Store.Log("start ui: " + ex.Message); }
        }
    }
}
