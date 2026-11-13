using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CLWallpaper.Host
{
    /// <summary>
    /// The background process ("--host"): owns the wallpaper windows and the tray icon.
    /// It only loads WinForms + Media Foundation, so it stays very light once the main window is closed.
    /// </summary>
    static class HostApp
    {
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        static extern int D3DKMTSetProcessSchedulingPriorityClass(IntPtr process, int priority);

        public static int Run(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, Ipc.HostMutex, out created))
            {
                if (!created) { Ipc.SetEvent(Ipc.ReloadEvent); return 0; }
                if (!Store.Init(args)) return 1;
                // A wallpaper must always yield to what the user is doing.
                try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                // Same for the GPU scheduler: games and browsers get the graphics card first.
                try { D3DKMTSetProcessSchedulingPriorityClass(Process.GetCurrentProcess().Handle, 1 /* BELOW_NORMAL */); } catch { }
                Application.EnableVisualStyles();
                try { Native.SetPreferredAppMode(1); Native.FlushMenuThemes(); } catch { }
                try { Application.Run(new HostContext()); }
                catch (Exception ex) { Store.Log("host crash: " + ex); }
                GC.KeepAlive(mutex);
            }
            return 0;
        }
    }

    sealed class MsgWindow : NativeWindow
    {
        public delegate bool Handler(ref Message m);
        readonly Handler handler;
        public MsgWindow(Handler h) { handler = h; CreateHandle(new CreateParams { Caption = "CLWallpaperHost" }); }
        protected override void WndProc(ref Message m) { if (handler(ref m)) return; base.WndProc(ref m); }
    }

    /// <summary>What one group of windows shows: all screens (shared mode) or a single screen (per-screen mode).</summary>
    sealed class Channel
    {
        public readonly List<WallpaperWindow> Wins = new List<WallpaperWindow>();
        public PlayState Content = new PlayState();
        public readonly Playlist List = new Playlist();
        public FolderSettings Folder = new FolderSettings();
        public string Current;
        public bool IsVideo, AllPaused;
        public double ImageElapsed;
        public int Cycles;
        // fade: 0 = none, 1 = fading out, 2 = fading in
        public int FadeStage;
        public double FadeT;
        public string Pending;
    }

    sealed class HostContext : ApplicationContext
    {
        const int WM_RELOAD = Native.WM_APP + 1, WM_QUITREQ = Native.WM_APP + 2, WM_CYCLE = Native.WM_APP + 3;
        const double FadeOutSec = 0.35, FadeInSec = 0.45, FadeInDelay = 0.12;

        readonly MsgWindow msg;
        readonly IntPtr msgHandle;
        readonly NotifyIcon tray;
        MenuItem miTitle, miPause, miNext;
        readonly List<WallpaperWindow> wins = new List<WallpaperWindow>();
        readonly List<Channel> channels = new List<Channel>();
        AppSettings settings = new AppSettings();
        PlayState state = new PlayState();
        LibraryData lib = new LibraryData();
        bool userPaused, first = true, quitting;
        int tickCount, rebuildAt = -1;
        string lastError = "";
        long lastErrorAt;
        readonly System.Windows.Forms.Timer timer, fadeTimer, loopTimer;
        // window events (foreground, minimize, move…) → re-check what covers the desktop right away
        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool UnhookWinEvent(IntPtr hook);
        readonly WinEventProc winEventProc;
        readonly List<IntPtr> hooks = new List<IntPtr>();
        bool pauseDirty;
        readonly EventWaitHandle evReload, evQuit;
        readonly RegisteredWaitHandle waitReload, waitQuit;
        readonly uint wmTaskbarCreated;
        // session state: nothing to show while the session is locked, viewed remotely, or the screens are off
        [System.Runtime.InteropServices.DllImport("wtsapi32.dll")] static extern bool WTSRegisterSessionNotification(IntPtr hwnd, int flags);
        [System.Runtime.InteropServices.DllImport("wtsapi32.dll")] static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr RegisterPowerSettingNotification(IntPtr h, ref Guid g, int flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool UnregisterPowerSettingNotification(IntPtr h);
        const int WM_WTSSESSION_CHANGE = 0x02B1, WM_POWERBROADCAST = 0x0218, PBT_POWERSETTINGCHANGE = 0x8013, PBT_APMRESUMEAUTOMATIC = 0x12;
        static Guid GUID_CONSOLE_DISPLAY_STATE = new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");
        bool locked, remote, displayOff;
        IntPtr powerNotify;

        public HostContext()
        {
            wmTaskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
            msg = new MsgWindow(OnMsg);
            msgHandle = msg.Handle;

            evReload = new EventWaitHandle(false, EventResetMode.AutoReset, Ipc.ReloadEvent);
            evQuit = new EventWaitHandle(false, EventResetMode.AutoReset, Ipc.QuitEvent);
            IntPtr h = msgHandle;
            waitReload = ThreadPool.RegisterWaitForSingleObject(evReload, delegate { Native.PostMessage(h, WM_RELOAD, IntPtr.Zero, IntPtr.Zero); }, null, -1, false);
            waitQuit = ThreadPool.RegisterWaitForSingleObject(evQuit, delegate { Native.PostMessage(h, WM_QUITREQ, IntPtr.Zero, IntPtr.Zero); }, null, -1, false);

            tray = new NotifyIcon();
            tray.Icon = LoadIcon();
            tray.Text = Store.AppName;
            BuildMenu();
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Ipc.StartUi(); };
            tray.Visible = true;

            timer = new System.Windows.Forms.Timer { Interval = 250 };
            timer.Tick += OnTick;
            timer.Start();
            fadeTimer = new System.Windows.Forms.Timer { Interval = 15 };
            fadeTimer.Tick += OnFadeTick;
            loopTimer = new System.Windows.Forms.Timer { Interval = 15 };
            loopTimer.Tick += delegate
            {
                bool need = false;
                foreach (var w in wins) if (w.Video != null && w.Video.NeedsFastTick) { w.Video.Tick(); need |= w.Video.NeedsFastTick; }
                if (!need) loopTimer.Stop();
            };
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

            winEventProc = OnWinEvent;
            const uint OUTOFCONTEXT_SKIPOWN = 0x0000 | 0x0002;
            hooks.Add(SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, winEventProc, 0, 0, OUTOFCONTEXT_SKIPOWN)); // foreground
            hooks.Add(SetWinEventHook(0x000A, 0x000B, IntPtr.Zero, winEventProc, 0, 0, OUTOFCONTEXT_SKIPOWN)); // move/size start-end
            hooks.Add(SetWinEventHook(0x0016, 0x0017, IntPtr.Zero, winEventProc, 0, 0, OUTOFCONTEXT_SKIPOWN)); // minimize start-end
            hooks.Add(SetWinEventHook(0x8002, 0x8003, IntPtr.Zero, winEventProc, 0, 0, OUTOFCONTEXT_SKIPOWN)); // show / hide
            hooks.Add(SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, winEventProc, 0, 0, OUTOFCONTEXT_SKIPOWN)); // location (maximize/restore)

            try { WTSRegisterSessionNotification(msgHandle, 0); } catch { }
            try { powerNotify = RegisterPowerSettingNotification(msgHandle, ref GUID_CONSOLE_DISPLAY_STATE, 0); } catch { }
            remote = SystemInformation.TerminalServerSession;

            // First load runs inside the message loop (so an early Quit() exits cleanly).
            Native.PostMessage(msgHandle, WM_RELOAD, IntPtr.Zero, IntPtr.Zero);
        }

        static Icon LoadIcon()
        {
            try
            {
                using (var s = typeof(HostContext).Assembly.GetManifestResourceStream("CLWallpaper.app.ico"))
                    return new Icon(s, SystemInformation.SmallIconSize);
            }
            catch { return SystemIcons.Application; }
        }

        void BuildMenu()
        {
            miTitle = new MenuItem(Store.AppName) { Enabled = false };
            var miOpen = new MenuItem("Ouvrir CL-Wallpaper", delegate { Ipc.StartUi(); }) { DefaultItem = true };
            miPause = new MenuItem("Mettre en pause", delegate { TogglePause(); });
            miNext = new MenuItem("Fond suivant", delegate { foreach (var c in channels) Next(c); });
            var miStop = new MenuItem("Arrêter le fond animé et quitter", delegate { StopAndQuit(); });
            tray.ContextMenu = new ContextMenu(new[] { miTitle, new MenuItem("-"), miOpen, miPause, miNext, new MenuItem("-"), miStop });
        }

        void UpdateTray()
        {
            var names = channels.Where(c => c.Current != null).Select(c => Path.GetFileName(c.Current)).Distinct().ToList();
            string name = names.Count == 0 ? "—" : string.Join(", ", names);
            miTitle.Text = (userPaused ? "En pause · " : "En cours · ") + name;
            miPause.Text = userPaused ? "Reprendre" : "Mettre en pause";
            miNext.Visible = channels.Any(c => c.Content.Kind == "folder" && c.List.Count > 1);
            string t = Store.AppName + " — " + name;
            tray.Text = t.Length > 63 ? t.Substring(0, 60) + "…" : t;
        }

        bool OnMsg(ref Message m)
        {
            if (m.Msg == WM_RELOAD) { Reload(); return true; }
            if (m.Msg == WM_QUITREQ) { Quit(); return true; }
            if (m.Msg == WM_CYCLE) { int i = m.WParam.ToInt32(); if (i >= 0 && i < channels.Count) OnCycle(channels[i]); return true; }
            if (m.Msg == (int)wmTaskbarCreated) { ScheduleRebuild(2000); return false; }
            if (m.Msg == WM_WTSSESSION_CHANGE)
            {
                int code = m.WParam.ToInt32();
                if (code == 7) locked = true;          // WTS_SESSION_LOCK
                else if (code == 8) locked = false;    // WTS_SESSION_UNLOCK
                remote = SystemInformation.TerminalServerSession;
                Store.Log("session event " + code + " locked=" + locked + " remote=" + remote);
                UpdatePause();
                WriteStatus();
                return false;
            }
            if (m.Msg == WM_POWERBROADCAST)
            {
                int ev = m.WParam.ToInt32();
                if (ev == PBT_POWERSETTINGCHANGE && m.LParam != IntPtr.Zero)
                {
                    // POWERBROADCAST_SETTING: GUID (16) + DataLength (4) + Data; 0 = off, 1 = on, 2 = dimmed
                    displayOff = System.Runtime.InteropServices.Marshal.ReadInt32(m.LParam, 20) == 0;
                    UpdatePause();
                    WriteStatus();
                }
                else if (ev == PBT_APMRESUMEAUTOMATIC) foreach (var w in wins) if (w.Video != null) w.Video.Repaint();
                return false;
            }
            return false;
        }

        void OnDisplayChanged(object sender, EventArgs e) { ScheduleRebuild(1500); }
        void ScheduleRebuild(int ms) { rebuildAt = Environment.TickCount + ms; }

        // ---------------------------------------------------------------- state

        PlayState Desired(int channelIndex)
        {
            var sch = Scheduled();
            if (sch != null) return sch;
            return settings.Monitors == 2 ? state.ForScreen(channelIndex) : state;
        }

        /// <summary>The day or night wallpaper, unless the user picked one by hand since the last switch.</summary>
        PlayState Scheduled()
        {
            if (!settings.ScheduleActive) return null;
            var now = DateTime.Now;
            var p = settings.IsNight(now) ? settings.Night : settings.Day;
            if (!p.IsSet) return null;
            if (state.IsSet && state.At > 0 && new DateTime(state.At, DateTimeKind.Utc).ToLocalTime() > settings.LastSwitch(now)) return null;
            return p;
        }

        /// <summary>Switches to whatever the schedule now asks for (called every few seconds).</summary>
        void CheckSchedule()
        {
            if (!settings.ScheduleActive) return;
            bool changed = false;
            for (int i = 0; i < channels.Count; i++)
            {
                var want = Desired(i);
                if (want.Same(channels[i].Content) || !want.IsSet) continue;
                channels[i].Content = Clone(want);
                StartContent(channels[i], true);
                changed = true;
            }
            if (changed) { UpdatePause(); UpdateTray(); WriteStatus(); }
        }

        void Reload()
        {
            if (quitting) return;
            var ns = Store.LoadSettings();
            var st = Store.LoadState();
            lib = Store.LoadLibrary();
            bool anything = st.IsSet || (ns.Monitors == 2 && st.Screens.Any(s => s.IsSet)) || ns.ScheduleActive;
            if (!anything) { Quit(); return; }

            bool rebuild = first || ns.Monitors != settings.Monitors;
            settings = ns;
            state = st;
            userPaused = st.Paused;
            first = false;
            if (rebuild) RebuildWindows();

            for (int i = 0; i < channels.Count; i++)
            {
                var c = channels[i];
                var want = Desired(i);
                if (!want.Same(c.Content) || c.Current == null && want.IsSet) { c.Content = Clone(want); StartContent(c, !rebuild); }
                else RefreshSettings(c);
            }
            UpdatePause();
            UpdateTray();
            WriteStatus();
        }

        static PlayState Clone(PlayState p) { return new PlayState { Kind = p.Kind, Path = p.Path }; }

        void StartContent(Channel c, bool fade)
        {
            if (c.Content.Kind == "folder")
            {
                c.Folder = lib.Folder(c.Content.Path);
                c.List.Build(c.Content.Path, c.Folder);
                if (c.List.Current == null) { ShowNothing(c); return; }
                Show(c, c.List.Current, fade);
            }
            else if (c.Content.Kind == "item" && File.Exists(Store.Full(c.Content.Path))) Show(c, c.Content.Path, fade);
            else
            {
                if (c.Content.Kind == "item") ReportError("Fichier introuvable : il a peut-être été déplacé ou supprimé.");
                ShowNothing(c);
            }
        }

        void RefreshSettings(Channel c)
        {
            if (c.Content.Kind == "folder")
            {
                var nf = lib.Folder(c.Content.Path);
                bool reshuffle = nf.Order != c.Folder.Order || nf.Recursive != c.Folder.Recursive;
                c.Folder = nf;
                if (reshuffle) c.List.Build(c.Content.Path, c.Folder, c.Current);
                else c.List.Refresh(c.Content.Path, c.Folder);
            }
            if (c.Current == null || !File.Exists(Store.Full(c.Current))) { StartContent(c, true); return; }
            var s = lib.Item(c.Current);
            for (int i = 0; i < c.Wins.Count; i++) c.Wins[i].UpdateSettings(s, Muted(c, i));
        }

        /// <summary>Only the first window of each channel plays sound.</summary>
        bool Muted(Channel c, int winIndex) { return settings.Mute || winIndex > 0; }

        void ShowNothing(Channel c)
        {
            c.Current = null;
            c.FadeStage = 0;
            foreach (var w in c.Wins) { w.Clear(); w.SetAlpha(255); }
        }

        void Show(Channel c, string rel, bool fade)
        {
            if (fade && settings.Fade && c.Current != null)
            {
                c.Pending = rel;
                c.FadeStage = 1;
                c.FadeT = 0;
                fadeTimer.Start();
                return;
            }
            DoShow(c, rel);
            if (settings.Fade) BeginFadeIn(c);
        }

        void DoShow(Channel c, string rel)
        {
            c.Current = rel;
            c.ImageElapsed = 0;
            c.Cycles = 0;
            string full = Store.Full(rel);
            c.IsVideo = Store.IsVideo(full);
            var s = lib.Item(rel);
            for (int i = 0; i < c.Wins.Count; i++)
            {
                try
                {
                    if (c.IsVideo) c.Wins[i].ShowVideo(full, s, Muted(c, i));
                    else c.Wins[i].ShowImage(full, s);
                }
                catch (Exception ex) { Store.Log("show " + rel + ": " + ex.Message); }
            }
            WriteStatus();
            UpdateTray();
        }

        void BeginFadeIn(Channel c)
        {
            foreach (var w in c.Wins) w.SetAlpha(0);
            c.FadeStage = 2;
            c.FadeT = -FadeInDelay; // let the first frame arrive before revealing it
            fadeTimer.Start();
        }

        void OnFadeTick(object sender, EventArgs e)
        {
            bool any = false;
            foreach (var c in channels)
            {
                if (c.FadeStage == 0) continue;
                any = true;
                if (c.FadeStage == 1)
                {
                    c.FadeT += 0.015 / FadeOutSec;
                    double a = Math.Max(0, 1 - c.FadeT);
                    foreach (var w in c.Wins) w.SetAlpha((byte)(255 * a * a));
                    if (c.FadeT >= 1)
                    {
                        string next = c.Pending; c.Pending = null;
                        if (next != null) DoShow(c, next);
                        foreach (var w in c.Wins) w.SetAlpha(0);
                        c.FadeStage = 2;
                        c.FadeT = -FadeInDelay;
                    }
                }
                else
                {
                    c.FadeT += 0.015 / FadeInSec;
                    double a = Math.Max(0, Math.Min(1, c.FadeT));
                    foreach (var w in c.Wins) w.SetAlpha((byte)(255 * (1 - (1 - a) * (1 - a))));
                    if (c.FadeT >= 1) { c.FadeStage = 0; foreach (var w in c.Wins) w.SetAlpha(255); }
                }
            }
            if (!any) fadeTimer.Stop();
        }

        void Next(Channel c)
        {
            if (c.Content.Kind != "folder" || c.List.Count == 0) return;
            string n = null;
            for (int i = 0; i < c.List.Count; i++)
            {
                n = c.List.Next();
                if (n != null && File.Exists(Store.Full(n))) break;
                n = null;
            }
            if (n == null) return;
            if (n == c.Current && c.IsVideo) { c.Cycles = 0; return; }
            Show(c, n, true);
        }

        void OnCycle(Channel c)
        {
            if (c.Content.Kind != "folder" || c.List.Count <= 1 || c.FadeStage == 1) return;
            c.Cycles++;
            if (c.Cycles >= Math.Max(1, c.Folder.VideoLoops)) Next(c);
        }

        void RebuildWindows()
        {
            DestroyWindows();
            if (!DesktopHost.Prepare()) { ScheduleRebuild(3000); return; }
            foreach (var b in ScreenBounds(settings.Monitors == 1))
            {
                try { wins.Add(new WallpaperWindow(new RECT { Left = b.Left, Top = b.Top, Right = b.Right, Bottom = b.Bottom })); }
                catch (Exception ex) { Store.Log("window: " + ex.Message); }
            }
            if (settings.Monitors == 2) foreach (var w in wins) { var c = new Channel(); c.Wins.Add(w); channels.Add(c); }
            else if (wins.Count > 0) { var c = new Channel(); c.Wins.AddRange(wins); channels.Add(c); }

            IntPtr h = msgHandle;
            for (int i = 0; i < channels.Count; i++)
            {
                IntPtr idx = new IntPtr(i);
                channels[i].Wins[0].Cycle += delegate { Native.PostMessage(h, WM_CYCLE, idx, IntPtr.Zero); };
            }
            foreach (var w in wins) w.Failed += ReportError;
        }

        /// <summary>
        /// Monitor rectangles. CLW_FAKE_SCREENS=2 splits the primary screen in two halves, to test the
        /// per-screen mode on a single monitor.
        /// </summary>
        static List<Rectangle> ScreenBounds(bool primaryOnly)
        {
            var l = new List<Rectangle>();
            if (Environment.GetEnvironmentVariable("CLW_FAKE_SCREENS") == "2")
            {
                var p = Screen.PrimaryScreen.Bounds;
                l.Add(new Rectangle(p.Left, p.Top, p.Width / 2, p.Height));
                if (!primaryOnly) l.Add(new Rectangle(p.Left + p.Width / 2, p.Top, p.Width - p.Width / 2, p.Height));
                return l;
            }
            foreach (var sc in primaryOnly ? new[] { Screen.PrimaryScreen } : Screen.AllScreens) l.Add(sc.Bounds);
            return l;
        }

        void DestroyWindows()
        {
            fadeTimer.Stop();
            foreach (var w in wins) { try { w.Dispose(); } catch { } }
            wins.Clear();
            channels.Clear();
            DesktopHost.Refresh();
        }

        void ReportError(string message)
        {
            lastError = message;
            lastErrorAt = DateTime.UtcNow.Ticks;
            WriteStatus();
            try { tray.ShowBalloonTip(6000, Store.AppName, message, ToolTipIcon.Warning); } catch { }
        }

        // ---------------------------------------------------------------- pause logic

        void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0 || hwnd == IntPtr.Zero) return; // window-level events only
            if (ev == 0x8002 && DesktopHost.IsWorkerW(hwnd)) { FixDesktopOrder(); return; } // new wallpaper layer (virtual desktop switch)
            if (ev == 0x800B) { pauseDirty = true; return; }   // very frequent: handled on the next tick
            if (ev == 0x0017 || ev == 0x0003) UpdatePause();   // restored / switched app: react immediately
            pauseDirty = true;                                 // and again once animations are done
        }

        void OnTick(object sender, EventArgs e)
        {
            tickCount++;
            FixDesktopOrder(); // fallback in case the show event was missed
            foreach (var w in wins)
                if (w.Video != null)
                {
                    w.Video.Tick();
                    if (w.Video.NeedsFastTick && !loopTimer.Enabled) loopTimer.Start();
                }

            if (rebuildAt >= 0 && unchecked(Environment.TickCount - rebuildAt) >= 0)
            {
                rebuildAt = -1;
                RebuildWindows();
                for (int i = 0; i < channels.Count; i++) { channels[i].Content = Clone(Desired(i)); StartContent(channels[i], false); }
                UpdatePause();
            }
            if (pauseDirty || tickCount % 8 == 0) { pauseDirty = false; UpdatePause(); }
            if (tickCount % 20 == 0) CheckSchedule();

            foreach (var c in channels)
            {
                if (c.Content.Kind != "folder" || c.Current == null || c.IsVideo || c.AllPaused || c.List.Count <= 1 || c.FadeStage != 0) continue;
                c.ImageElapsed += 0.25;
                if (c.ImageElapsed >= Math.Max(2, c.Folder.ImageSec)) Next(c);
            }
        }

        readonly HashSet<IntPtr> myHandles = new HashSet<IntPtr>();

        void FixDesktopOrder()
        {
            if (wins.Count == 0) return;
            myHandles.Clear();
            foreach (var w in wins) myHandles.Add(w.Handle);
            if (DesktopHost.FixOrder(myHandles))
                foreach (var w in wins) if (w.Video != null) w.Video.Repaint();
        }

        void UpdatePause()
        {
            bool battery = settings.PauseBattery && SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
            battery |= locked || remote || displayOff; // nobody can see the desktop
            bool[] covered = PauseDetector.Covered(wins, settings);
            for (int i = 0; i < wins.Count; i++) wins[i].SetPaused(userPaused || battery || covered[i]);
            foreach (var c in channels)
                c.AllPaused = userPaused || battery || c.Wins.All(w => covered[wins.IndexOf(w)]);
        }

        void TogglePause()
        {
            userPaused = !userPaused;
            var st = Store.LoadState();
            st.Paused = userPaused;
            Store.SaveState(st);
            state.Paused = userPaused;
            UpdatePause();
            UpdateTray();
            WriteStatus();
        }

        void WriteStatus()
        {
            var currents = channels.Select(c => (object)(c.Current ?? "")).ToList();
            var first = channels.FirstOrDefault(c => c.Current != null);
            Store.SaveStatus(new Dictionary<string, object> {
                { "current", first != null ? first.Current : "" }, { "currents", currents },
                { "kind", state.Kind }, { "path", state.Path }, { "screens", channels.Count },
                { "paused", userPaused }, { "hidden", locked || remote || displayOff }, { "error", lastError }, { "errorAt", (double)lastErrorAt },
                { "pid", Process.GetCurrentProcess().Id } });
        }

        void StopAndQuit()
        {
            var st = Store.LoadState();
            st.Kind = "none";
            st.Screens.Clear();
            Store.SaveState(st);
            Quit();
        }

        void Quit()
        {
            if (quitting) return;
            quitting = true;
            timer.Stop();
            loopTimer.Stop();
            foreach (var h in hooks) if (h != IntPtr.Zero) UnhookWinEvent(h);
            try { WTSUnRegisterSessionNotification(msgHandle); } catch { }
            if (powerNotify != IntPtr.Zero) { try { UnregisterPowerSettingNotification(powerNotify); } catch { } powerNotify = IntPtr.Zero; }
            hooks.Clear();
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            DestroyWindows();
            try { Store.SaveStatus(new Dictionary<string, object> { { "current", "" }, { "kind", "none" }, { "path", "" }, { "paused", false } }); } catch { }
            tray.Visible = false;
            tray.Dispose();
            waitReload.Unregister(null);
            waitQuit.Unregister(null);
            msg.DestroyHandle();
            ExitThread();
        }
    }

    /// <summary>Builds the ordered/shuffled list of media for a folder playlist.</summary>
    sealed class Playlist
    {
        List<string> items = new List<string>();
        int index;
        readonly Random rnd = new Random();
        FolderSettings fs = new FolderSettings();

        public int Count { get { return items.Count; } }
        public int Order { get { return fs.Order; } }
        public string Current { get { return items.Count == 0 ? null : items[index]; } }

        public void Build(string folderRel, FolderSettings settings, string keep = null)
        {
            fs = settings;
            items = Store.SortedMedia(Store.Full(folderRel), fs.Recursive).Select(Store.Rel).ToList();
            if (fs.Order == 1) Shuffle(null);
            index = 0;
            if (keep != null)
            {
                int k = items.FindIndex(x => string.Equals(x, keep, StringComparison.OrdinalIgnoreCase));
                if (k >= 0) index = k;
            }
        }

        /// <summary>Re-scans the folder, keeping the current order and position.</summary>
        public void Refresh(string folderRel, FolderSettings settings)
        {
            fs = settings;
            var now = Store.SortedMedia(Store.Full(folderRel), fs.Recursive).Select(Store.Rel).ToList();
            var set = new HashSet<string>(now, StringComparer.OrdinalIgnoreCase);
            string cur = Current;
            var kept = items.Where(set.Contains).ToList();
            var known = new HashSet<string>(kept, StringComparer.OrdinalIgnoreCase);
            kept.AddRange(now.Where(x => !known.Contains(x)));
            items = kept;
            index = cur == null ? 0 : Math.Max(0, items.FindIndex(x => string.Equals(x, cur, StringComparison.OrdinalIgnoreCase)));
        }

        public string Next()
        {
            if (items.Count == 0) return null;
            index++;
            if (index >= items.Count)
            {
                index = 0;
                if (fs.Order == 1) Shuffle(items[items.Count - 1]);
            }
            return items[index];
        }

        void Shuffle(string avoidFirst)
        {
            for (int i = items.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); var t = items[i]; items[i] = items[j]; items[j] = t; }
            if (avoidFirst != null && items.Count > 1 && items[0] == avoidFirst) { var t = items[0]; items[0] = items[1]; items[1] = t; }
        }
    }

    /// <summary>Detects monitors hidden by a fullscreen or maximized window, to pause the wallpaper there.</summary>
    static class PauseDetector
    {
        static readonly HashSet<string> skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
            "ApplicationManager_ImmersiveShellWindow", "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland" };
        static readonly uint myPid = (uint)Process.GetCurrentProcess().Id;

        public static bool[] Covered(List<WallpaperWindow> wins, AppSettings s)
        {
            var res = new bool[wins.Count];
            if (wins.Count == 0 || (!s.PauseFullscreen && !s.PauseMaximized)) return res;
            var sb = new StringBuilder(128);
            Native.EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
                long ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                if ((ex & (Native.WS_EX_TOOLWINDOW | Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE)) != 0) return true;
                int cloaked;
                if (Native.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                if (pid == myPid) return true;
                sb.Length = 0; Native.GetClassName(h, sb, 128);
                if (skip.Contains(sb.ToString())) return true;
                RECT r;
                if (!Native.GetWindowRect(h, out r) || r.Width < 200 || r.Height < 200) return true;
                bool zoomed = s.PauseMaximized && Native.IsZoomed(h);
                int cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
                for (int i = 0; i < wins.Count; i++)
                {
                    RECT m = wins[i].Bounds;
                    if (s.PauseFullscreen && r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom) res[i] = true;
                    else if (zoomed && cx >= m.Left && cx < m.Right && cy >= m.Top && cy < m.Bottom) res[i] = true;
                    else if (s.PauseMaximized)
                    {
                        // a big, non-maximized window (snapped or resized browser…) hiding ≥ 85 % of the screen
                        long iw = Math.Max(0, Math.Min(r.Right, m.Right) - Math.Max(r.Left, m.Left));
                        long ih = Math.Max(0, Math.Min(r.Bottom, m.Bottom) - Math.Max(r.Top, m.Top));
                        if (iw * ih >= 0.85 * m.Width * (long)m.Height) res[i] = true;
                    }
                }
                return true;
            }, IntPtr.Zero);
            return res;
        }
    }
}
