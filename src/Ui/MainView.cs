using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;

namespace CLWallpaper.Ui
{
    /// <summary>Main window controller (library browser, inspector, sheets).</summary>
    sealed partial class MainView
    {
        public readonly Window Window;
        readonly ResourceDictionary appRes;
        AppSettings settings;
        LibraryData lib = new LibraryData();
        ThumbService thumbs;
        FileSystemWatcher watcher;
        EventWaitHandle showEvent;
        readonly DispatcherTimer watchTimer, statusTimer, saveTimer, toastTimer, previewTimer;
        readonly ObservableCollection<FolderVM> roots = new ObservableCollection<FolderVM>();
        readonly ObservableCollection<EntryVM> entries = new ObservableCollection<EntryVM>();
        readonly ICollectionView view;
        string currentRel = "";
        readonly Stack<string> back = new Stack<string>(), fwd = new Stack<string>();
        bool suppressTree, configured, libDirty, settingsDirty, notifyPending, inspectorVisible = true;

        // wallpaper host status
        string activeRel, activeKind = "none", activePath = "";
        readonly HashSet<string> activeRels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        double lastErrorAt;
        bool hostRunning, hostPaused;
        DateTime statusStamp;

        // ---- named XAML elements (bound by name through reflection) ----
#pragma warning disable 649
        Grid Root, SheetLayer, DropZone, SidebarTop, RRestoreGlyph;
        Border NowThumb, Toast, DropOverlay, InspectorPane, PreviewImage, RetroTitle, RMaxGlyph;
        ContentControl SheetCard, WinFrame;
        StackPanel InspItem, InspFolder, InspMulti, VideoOpts, PausePanel, SetupPanel, SettingsPanel, PromptPanel, MovePanel, Empty, NowButtons,
            Lights, Brand, CaptionPanel;
        DockPanel ToolbarDock;
        Image BrandIcon, RetroIcon;
        TreeView Tree;
        ListBox List, MoveList;
        TextBox Search, SetupPath, PromptInput;
        TextBlock TitleText, SubtitleText, NowLabel, NowName, NowGlyph, ToastText, InspName, InspSub, ApplyGlyph, ApplyText, TrimInfo,
            TrimStartText, TrimEndText, PauseText, VolIcon, VolText, FolderName, FolderSub, PlayFolderGlyph, PlayFolderText, ImgDurText,
            MultiText, PromptTitle, PromptMsg, PromptError, SetupError, DataPathText, VersionText, DropText, EmptyTitle, EmptySub, MoveTitle, StyleHint, PreviewError;
        Button CBtnMin, CBtnMax, CBtnClose, RBtnMin, RBtnMax, RBtnClose;
        Button BtnClose, BtnMin, BtnMax, BtnNowPause, BtnNowStop, BtnSettings, BtnTheme, BtnNewFolderSide, BtnBack, BtnFwd, BtnNewFolder,
            BtnInspector, BtnImport, BtnApply, BtnReset, BtnPlayFolder, BtnMultiMove, BtnMultiDelete, BtnSetupBrowse, BtnSetupGo,
            BtnSettingsDone, BtnOpenData, BtnPromptCancel, BtnPromptOk, BtnMoveCancel, BtnMoveOk;
        Slider ZoomSlider, PauseSlider, VolSlider, ImgDurSlider;
        CheckBox SwFade, SwRecursive, ChkInstall, ChkStartMenu, ChkDesktop, SwAutostart, SwMute, SwFullscreen, SwMaximized, SwBattery;
        ContentControl SegFitSlot, TrimSlot, SegSpeedSlot, SegLoopSlot, SegOrderSlot, SegLoopsSlot, SegThemeSlot, SegMonitorsSlot, PreviewSlot, SegStyleSlot;
        ColumnDefinition InspCol;
        TextBox DayTime, NightTime, UpdateSource;
        TextBlock DayName, NightName, UpdateStatus;
        Button BtnStore, BtnCheckUpdate, BtnUpdateFile;
        CheckBox SwLoopFade, SwConvertGif, SwSchedule, SwUpdateCheck;
        Border LoopFadeBox;
        ScrollViewer SettingsScroll;
        Image SetupLogo;
#pragma warning restore 649

        Segmented segFit, segSpeed, segLoop, segOrder, segLoops, segTheme, segMonitors, segStyle;
        RangeSlider trim;

        public MainView(bool configured)
        {
            this.configured = configured;
            appRes = Application.Current.Resources;
            settings = configured ? Store.LoadSettings() : new AppSettings();
            Theme.Apply(appRes, settings.Style, Theme.Resolve(settings.Theme));

            Window = (Window)Res.Xaml("MainWindow.xaml");
            BindNames();
            try
            {
                Window.Icon = Res.Icon(32);
                SetupLogo.Source = Res.Icon(256);
                BrandIcon.Source = Res.Icon(32);
                RetroIcon.Source = Res.Icon(16);
            }
            catch { }

            watchTimer = Timer(700, delegate { watchTimer.Stop(); if (Store.IsInitialized) RefreshAll(); });
            statusTimer = Timer(1000, delegate { PollStatus(); });
            saveTimer = Timer(450, delegate { FlushSave(); });
            toastTimer = Timer(2300, delegate { toastTimer.Stop(); Toast.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260))); });
            previewTimer = Timer(250, delegate
            {
                var p = PreviewPlayer;
                if (p == null) return;
                p.Tick();
                previewTimer.Interval = TimeSpan.FromMilliseconds(p.NeedsFastTick ? 15 : 250);
            });
            BuildControls();
            WireChrome();
            WireLibrary();
            WireInspector();
            WireSheets();
            WireExtras();

            Tree.ItemsSource = roots;
            List.ItemsSource = entries;
            view = CollectionViewSource.GetDefaultView(entries);
            view.Filter = o => Search.Text.Length == 0 || ((EntryVM)o).Name.IndexOf(Search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0;
            ZoomSlider.Value = Math.Max(150, Math.Min(320, settings.CardSize));
            ApplyCardSize();
            ApplyChrome();
            ShowPanel("folder");

            Window.Loaded += delegate
            {
                if (this.configured) InitLibrary(); else ShowSetup();
            };
            StartShowListener();
        }

        DispatcherTimer Timer(int ms, EventHandler tick)
        {
            var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += tick;
            return t;
        }

        void BindNames()
        {
            foreach (var f in GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (!typeof(DependencyObject).IsAssignableFrom(f.FieldType)) continue;
                object o = Window.FindName(f.Name);
                if (o != null && f.FieldType.IsInstanceOfType(o)) f.SetValue(this, o);
            }
        }

        void BuildControls()
        {
            segFit = new Segmented("Remplir", "Ajuster", "Étirer"); SegFitSlot.Content = segFit;
            segSpeed = new Segmented("0,5×", "0,75×", "1×", "1,5×", "2×"); SegSpeedSlot.Content = segSpeed;
            segLoop = new Segmented("En boucle", "Une fois", "Avec pause"); SegLoopSlot.Content = segLoop;
            segOrder = new Segmented("Dans l'ordre", "Aléatoire"); SegOrderSlot.Content = segOrder;
            segLoops = new Segmented("1", "2", "3", "5", "10"); SegLoopsSlot.Content = segLoops;
            segTheme = new Segmented("Système", "Clair", "Sombre"); SegThemeSlot.Content = segTheme;
            segMonitors = new Segmented("Même fond partout", "Écran principal", "Un fond par écran"); SegMonitorsSlot.Content = segMonitors;
            segStyle = new Segmented(Theme.Names); SegStyleSlot.Content = segStyle;
            trim = new RangeSlider(); TrimSlot.Content = trim;
        }

        // ------------------------------------------------------------------ chrome / theme

        void WireChrome()
        {
            Window.SourceInitialized += delegate { Theme.ApplyWindow(Window); };
            Window.Activated += delegate { SetLights(true); };
            Window.Deactivated += delegate { SetLights(false); };
            Window.StateChanged += delegate
            {
                WinFrame.Margin = Window.WindowState == WindowState.Maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
                UpdateMaxGlyphs();
                if (PreviewPlayer != null) PreviewPlayer.SetPaused(Window.WindowState == WindowState.Minimized);
            };
            Window.Closing += delegate
            {
                FlushSave();
                StopPreview();
                if (watcher != null) watcher.Dispose();
                statusTimer.Stop();
            };
            Window.PreviewKeyDown += OnKey;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (settings.Theme == 0) Window.Dispatcher.BeginInvoke(new Action(ApplyTheme));
            };

            // the three sets of window buttons (macOS lights, Windows caption, Windows 98 title bar)
            foreach (var b in new[] { BtnClose, CBtnClose, RBtnClose }) b.Click += delegate { Window.Close(); };
            foreach (var b in new[] { BtnMin, CBtnMin, RBtnMin }) b.Click += delegate { Window.WindowState = WindowState.Minimized; };
            foreach (var b in new[] { BtnMax, CBtnMax, RBtnMax })
                b.Click += delegate { Window.WindowState = Window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; };
            BtnTheme.Click += delegate { settings.Theme = Theme.IsDark ? 1 : 2; SaveSettingsNow(); ApplyTheme(); };
            BtnSettings.Click += delegate { ShowSettings(); };
            BtnInspector.Click += delegate
            {
                inspectorVisible = !inspectorVisible;
                InspCol.Width = new GridLength(inspectorVisible ? 304 : 0);
                InspectorPane.Visibility = inspectorVisible ? Visibility.Visible : Visibility.Collapsed;
                if (!inspectorVisible) StopPreview(); else OnSelection();
            };
            BtnNowPause.Click += delegate { TogglePause(); };
            BtnNowStop.Click += delegate { StopWallpaper(); };
        }

        void SetLights(bool active)
        {
            string off = Theme.IsDark ? "#4B4B4F" : "#D3D3D7";
            BtnClose.Background = Brush(active ? "#FF5F57" : off);
            BtnMin.Background = Brush(active ? "#FEBC2E" : off);
            BtnMax.Background = Brush(active ? "#28C840" : off);
            RetroTitle.SetResourceReference(Border.BackgroundProperty, active ? "TitleActive" : "TitleInactive");
        }

        void UpdateMaxGlyphs()
        {
            bool max = Window.WindowState == WindowState.Maximized;
            CBtnMax.Content = max ? "" : "";
            CBtnMax.ToolTip = RBtnMax.ToolTip = max ? "Restaurer" : "Agrandir";
            RMaxGlyph.Visibility = max ? Visibility.Collapsed : Visibility.Visible;
            RRestoreGlyph.Visibility = max ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Shows the window chrome that matches the current interface (macOS / Base / Old School).</summary>
        void ApplyChrome()
        {
            int st = Theme.Style;
            bool retro = st == Theme.Retro;
            Lights.Visibility = st == Theme.Mac ? Visibility.Visible : Visibility.Collapsed;
            Brand.Visibility = st == Theme.Base ? Visibility.Visible : Visibility.Collapsed;
            SidebarTop.Visibility = retro ? Visibility.Collapsed : Visibility.Visible;
            CaptionPanel.Visibility = st == Theme.Base ? Visibility.Visible : Visibility.Collapsed;
            RetroTitle.Visibility = retro ? Visibility.Visible : Visibility.Collapsed;
            ToolbarDock.Margin = new Thickness(10, 0, st == Theme.Base ? 14 + 3 * 46 : 14, 0);
            WindowChrome.SetWindowChrome(Window, new WindowChrome
            {
                CaptionHeight = retro ? 24 : 52,
                ResizeBorderThickness = new Thickness(retro ? 4 : 6),
                GlassFrameThickness = Theme.UsesMica ? new Thickness(-1) : new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });
            TextOptions.SetTextFormattingMode(Window, retro ? TextFormattingMode.Display : TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(Window, retro ? TextRenderingMode.Aliased : TextRenderingMode.Auto);
            Theme.ApplyWindow(Window);
            VideoHost.Radius = retro ? 0 : (st == Theme.Base ? 12 : 10);
            if (preview != null) preview.UpdateRegion();
            BtnTheme.Content = Theme.IsDark ? "" : "";
            SetLights(Window.IsActive);
            UpdateMaxGlyphs();
        }

        static SolidColorBrush Brush(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b;
        }

        void ApplyTheme()
        {
            Theme.Apply(appRes, settings.Style, Theme.Resolve(settings.Theme));
            ApplyChrome();
            if (segTheme != null && segTheme.Selected != settings.Theme) segTheme.Selected = settings.Theme;
            if (segStyle != null && segStyle.Selected != settings.Style) segStyle.Selected = settings.Style;
        }

        void ApplyCardSize()
        {
            double w = Math.Round(ZoomSlider.Value);
            appRes["CardW"] = w;
            appRes["CardH"] = Math.Round(w * 9 / 16);
        }

        void StartShowListener()
        {
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Ipc.ShowEvent);
            ThreadPool.RegisterWaitForSingleObject(showEvent, delegate
            {
                Window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal;
                    Window.Show();
                    Window.Activate();
                    Window.Topmost = true; Window.Topmost = false;
                }));
            }, null, -1, false);
        }

        // ------------------------------------------------------------------ library / navigation

        void InitLibrary()
        {
            if (thumbs == null) thumbs = new ThumbService(Window.Dispatcher);
            lib = Store.LoadLibrary();
            BuildTree();
            Navigate("", false);
            StartWatcher();
            PollStatus();
            statusTimer.Start();
            var st = Store.LoadState();
            if (st.Kind != "none" && !Ipc.HostRunning) Ipc.StartHost();
        }

        void StartWatcher()
        {
            if (watcher != null) return;
            try
            {
                watcher = new FileSystemWatcher(Store.LibraryDir)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size
                };
                FileSystemEventHandler h = (s, e) => Window.Dispatcher.BeginInvoke(new Action(() => { watchTimer.Stop(); watchTimer.Start(); }));
                watcher.Created += h; watcher.Deleted += h; watcher.Changed += h;
                watcher.Renamed += (s, e) => h(s, e);
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { Store.Log("watcher: " + ex.Message); }
        }

        IEnumerable<FolderVM> AllFolders()
        {
            var stack = new Stack<FolderVM>(roots.Reverse());
            while (stack.Count > 0)
            {
                var f = stack.Pop();
                yield return f;
                foreach (var c in f.Children.Reverse()) stack.Push(c);
            }
        }

        void BuildTree()
        {
            var expanded = new HashSet<string>(AllFolders().Where(f => f.IsExpanded).Select(f => f.Rel), StringComparer.OrdinalIgnoreCase);
            suppressTree = true;
            roots.Clear();
            var root = new FolderVM { Name = "Tous les fonds", Rel = "", Depth = 0, Glyph = "" };
            root.Count = CountMedia(Store.LibraryDir);
            roots.Add(root);
            foreach (var d in Store.SortedDirs(Store.LibraryDir)) roots.Add(MakeFolder(d, 0, null, expanded));
            suppressTree = false;
            SelectTreeNode(currentRel);
        }

        FolderVM MakeFolder(string dir, int depth, FolderVM parent, HashSet<string> expanded)
        {
            var vm = new FolderVM { Name = Path.GetFileName(dir), Rel = Store.Rel(dir), Depth = depth, Glyph = "", Parent = parent };
            vm.Count = CountMedia(dir);
            foreach (var sub in Store.SortedDirs(dir)) vm.Children.Add(MakeFolder(sub, depth + 1, vm, expanded));
            vm.IsExpanded = expanded.Contains(vm.Rel);
            return vm;
        }

        static int CountMedia(string dir)
        {
            try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count(Store.IsMedia); }
            catch { return 0; }
        }

        void SelectTreeNode(string rel)
        {
            suppressTree = true;
            foreach (var f in AllFolders()) f.IsSelected = false;
            var t = AllFolders().FirstOrDefault(f => string.Equals(f.Rel, rel, StringComparison.OrdinalIgnoreCase));
            if (t != null)
            {
                for (var p = t.Parent; p != null; p = p.Parent) p.IsExpanded = true;
                t.IsSelected = true;
            }
            suppressTree = false;
        }

        void Navigate(string rel, bool push)
        {
            if (rel == null || !Directory.Exists(Store.Full(rel))) rel = "";
            if (push && !string.Equals(rel, currentRel, StringComparison.OrdinalIgnoreCase)) { back.Push(currentRel); fwd.Clear(); }
            currentRel = rel;
            if (Search.Text.Length > 0) Search.Text = "";
            LoadEntries(null);
            SelectTreeNode(rel);
            BtnBack.IsEnabled = back.Count > 0;
            BtnFwd.IsEnabled = fwd.Count > 0;
        }

        void GoBack()
        {
            if (back.Count == 0) return;
            fwd.Push(currentRel);
            Navigate(back.Pop(), false);
        }

        void GoForward()
        {
            if (fwd.Count == 0) return;
            back.Push(currentRel);
            Navigate(fwd.Pop(), false);
        }

        void LoadEntries(ICollection<string> keep)
        {
            if (thumbs == null) return;
            thumbs.CancelPending();
            entries.Clear();
            string dir = Store.Full(currentRel);
            foreach (var d in Store.SortedDirs(dir)) entries.Add(EntryVM.ForFolder(d));
            foreach (var f in Store.SortedMedia(dir, false)) entries.Add(EntryVM.ForFile(f));
            foreach (var e in entries) thumbs.Request(e);
            UpdateActiveFlags();
            TitleText.Text = FolderDisplay(currentRel);
            int folders = entries.Count(x => x.IsFolder), media = entries.Count - folders;
            var parts = new List<string>();
            if (folders > 0) parts.Add(folders + (folders > 1 ? " dossiers" : " dossier"));
            parts.Add(media + (media > 1 ? " fonds" : " fond"));
            SubtitleText.Text = string.Join(" · ", parts);
            if (keep != null) foreach (var e in entries) if (keep.Contains(e.Rel)) List.SelectedItems.Add(e);
            UpdateEmpty();
            OnSelection();
        }

        void RefreshAll()
        {
            var keep = new HashSet<string>(SelectedEntries().Select(x => x.Rel), StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(Store.Full(currentRel))) currentRel = "";
            BuildTree();
            LoadEntries(keep);
        }

        void UpdateEmpty()
        {
            bool empty = view.IsEmpty;
            Empty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            if (Search.Text.Length > 0)
            {
                EmptyTitle.Text = "Aucun résultat";
                EmptySub.Text = "Aucun fond ne correspond à « " + Search.Text.Trim() + " ».";
            }
            else
            {
                EmptyTitle.Text = "Glissez vos vidéos et images ici";
                EmptySub.Text = "ou cliquez sur Importer";
            }
        }

        static string FolderDisplay(string rel) { return string.IsNullOrEmpty(rel) ? "Tous les fonds" : Path.GetFileName(rel); }

        List<EntryVM> SelectedEntries() { return List.SelectedItems.Cast<EntryVM>().ToList(); }

        // ------------------------------------------------------------------ host status

        void PollStatus()
        {
            bool running = Ipc.HostRunning;
            DateTime stamp = Store.StatusTime;
            if (running == hostRunning && stamp == statusStamp) return;
            statusStamp = stamp;
            string cur = null, kind = "none", path = "";
            bool paused = false;
            activeRels.Clear();
            if (running)
            {
                var st = Store.LoadStatus();
                if (st != null)
                {
                    cur = Json.S(st, "current", ""); kind = Json.S(st, "kind", "none");
                    path = Json.S(st, "path", ""); paused = Json.B(st, "paused", false);
                    if (cur.Length == 0) cur = null;
                    object list;
                    if (st.TryGetValue("currents", out list) && list is List<object>)
                        foreach (var o in (List<object>)list) { var s = o as string; if (!string.IsNullOrEmpty(s)) activeRels.Add(s); }
                    // errors reported by the wallpaper (missing codec, unreadable file…)
                    double at = Json.D(st, "errorAt", 0);
                    string err = Json.S(st, "error", "");
                    if (at > lastErrorAt && err.Length > 0)
                    {
                        if (lastErrorAt > 0 || DateTime.UtcNow.Ticks - (long)at < TimeSpan.TicksPerMinute) ShowToast("⚠ " + err, 7000);
                        lastErrorAt = at;
                    }
                }
            }
            if (cur != null) activeRels.Add(cur);
            hostRunning = running; activeRel = cur; activeKind = kind; activePath = path; hostPaused = paused;
            UpdateNowCard();
            UpdateActiveFlags();
            RefreshInspectorButtons();
        }

        void ForceStatusRefresh() { statusStamp = DateTime.MinValue; hostRunning = !Ipc.HostRunning; }

        void UpdateNowCard()
        {
            bool on = hostRunning && activeKind != "none";
            NowButtons.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            NowThumb.SetResourceReference(Border.BackgroundProperty, "ThumbBg");
            NowGlyph.Visibility = Visibility.Visible;
            if (!on)
            {
                NowLabel.Text = "Aucun fond animé";
                NowName.Text = "Choisissez un fond";
                return;
            }
            NowLabel.Text = hostPaused ? "En pause" : (activeKind == "folder" ? "Dossier · " + FolderDisplay(activePath) : "En lecture");
            NowName.Text = activeRel != null ? Path.GetFileNameWithoutExtension(activeRel) : "—";
            BtnNowPause.Content = hostPaused ? "" : "";
            BtnNowPause.ToolTip = hostPaused ? "Reprendre" : "Pause";
            if (activeRel != null && thumbs != null)
            {
                string want = activeRel;
                thumbs.Request(Store.Full(activeRel), true, (img, d, w, h) =>
                {
                    if (img == null || want != activeRel) return;
                    NowThumb.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
                    NowGlyph.Visibility = Visibility.Collapsed;
                });
            }
        }

        void UpdateActiveFlags()
        {
            foreach (var e in entries)
            {
                bool a = false;
                if (hostRunning)
                {
                    if (e.IsFolder) a = activeKind == "folder" && string.Equals(activePath, e.Rel, StringComparison.OrdinalIgnoreCase);
                    else a = activeRels.Contains(e.Rel);
                }
                e.IsActive = a;
            }
        }

        bool AffectsActive(string rel)
        {
            if (!hostRunning) return false;
            if (activeRels.Contains(rel)) return true;
            return activeKind == "folder" && Store.IsSameOrChild(rel, activePath);
        }

        // ------------------------------------------------------------------ actions

        void ApplyItem(EntryVM e)
        {
            if (e == null || e.IsFolder) return;
            ChooseScreen(screen =>
            {
                SetTarget("item", e.Rel, screen);
                activeRel = e.Rel; activeRels.Add(e.Rel);
                if (screen < 0) { activeKind = "item"; activePath = e.Rel; }
                UpdateActiveFlags(); UpdateNowCard(); RefreshInspectorButtons();
                ShowToast(screen < 0 ? "Fond d'écran appliqué" : "Fond appliqué sur l'écran " + (screen + 1));
            });
        }

        void ApplyFolder(string rel)
        {
            var fs = lib.Folder(rel);
            if (Store.SortedMedia(Store.Full(rel), fs.Recursive).Count == 0) { ShowToast("Ce dossier ne contient aucun fond"); return; }
            ChooseScreen(screen =>
            {
                SetTarget("folder", rel, screen);
                if (screen < 0) { activeKind = "folder"; activePath = rel; }
                UpdateActiveFlags(); RefreshInspectorButtons();
                ShowToast("Lecture du dossier « " + FolderDisplay(rel) + " »" + (screen < 0 ? "" : " sur l'écran " + (screen + 1)));
            });
        }

        /// <summary>In "un fond par écran" mode with several screens, asks which screen to use (-1 = all).</summary>
        void ChooseScreen(Action<int> pick)
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (settings.Monitors != 2 || screens.Length < 2) { pick(-1); return; }
            var cm = new ContextMenu();
            for (int i = 0; i < screens.Length; i++)
            {
                int idx = i;
                var b = screens[i].Bounds;
                cm.Items.Add(MI("Écran " + (i + 1) + (screens[i].Primary ? " (principal)" : "") + " · " + b.Width + "×" + b.Height, "", () => pick(idx)));
            }
            cm.Items.Add(new Separator());
            cm.Items.Add(MI("Tous les écrans", "", () => pick(-1)));
            cm.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            cm.IsOpen = true;
        }

        void SetTarget(string kind, string path, int screen)
        {
            FlushSave();
            var st = Store.LoadState();
            if (screen < 0) { st.Kind = kind; st.Path = path; st.Screens.Clear(); }
            else
            {
                while (st.Screens.Count <= screen) st.Screens.Add(new PlayState());
                st.Screens[screen] = new PlayState { Kind = kind, Path = path };
                if (!st.IsSet) { st.Kind = kind; st.Path = path; }
            }
            st.Paused = false;
            st.At = DateTime.UtcNow.Ticks; // a manual choice wins over the day/night schedule until its next switch
            Store.SaveState(st);
            Ipc.NotifyHost(true);
            hostRunning = true; hostPaused = false;
            statusStamp = DateTime.MinValue;
        }

        void TogglePause()
        {
            var st = Store.LoadState();
            st.Paused = !hostPaused;
            Store.SaveState(st);
            Ipc.NotifyHost(false);
            hostPaused = st.Paused;
            UpdateNowCard();
        }

        void StopWallpaper()
        {
            var st = Store.LoadState();
            st.Kind = "none";
            st.Screens.Clear();
            Store.SaveState(st);
            Ipc.NotifyHost(false);
            hostRunning = false; activeRel = null; activeKind = "none"; activeRels.Clear();
            UpdateNowCard(); UpdateActiveFlags(); RefreshInspectorButtons();
            ShowToast("Fond animé arrêté");
        }

        void StopHostAndWait()
        {
            var st = Store.LoadState();
            st.Kind = "none";
            Store.SaveState(st);
            Ipc.NotifyHost(false);
            for (int i = 0; i < 60 && Ipc.HostRunning; i++) Thread.Sleep(50);
        }

        // ------------------------------------------------------------------ persistence

        void MarkDirty(bool notifyHost)
        {
            libDirty = true;
            if (notifyHost) notifyPending = true;
            saveTimer.Stop(); saveTimer.Start();
        }

        void FlushSave()
        {
            saveTimer.Stop();
            if (!Store.IsInitialized) return;
            if (libDirty) { Store.SaveLibrary(lib); libDirty = false; }
            if (settingsDirty) { Store.SaveSettings(settings); settingsDirty = false; }
            if (notifyPending) { notifyPending = false; Ipc.NotifyHost(false); }
        }

        void SaveSettingsNow()
        {
            settingsDirty = false;
            if (Store.IsInitialized) Store.SaveSettings(settings);
        }

        // ------------------------------------------------------------------ toast & keyboard

        void ShowToast(string text) { ShowToast(text, 2300); }

        void ShowToast(string text, int ms)
        {
            ToastText.Text = text;
            Toast.MaxWidth = 560;
            ToastText.TextWrapping = TextWrapping.Wrap;
            Toast.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
            toastTimer.Stop();
            toastTimer.Interval = TimeSpan.FromMilliseconds(ms);
            toastTimer.Start();
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (SheetLayer.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape && sheetCancelable) { CloseSheet(); e.Handled = true; }
                else if (e.Key == Key.Enter && PromptPanel.Visibility == Visibility.Visible) { PromptOk(); e.Handled = true; }
                else if (e.Key == Key.Enter && SetupPanel.Visibility == Visibility.Visible) { FinishSetup(); e.Handled = true; }
                return;
            }
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (Keyboard.FocusedElement is TextBox)
            {
                if (e.Key == Key.Escape) { Search.Text = ""; List.Focus(); e.Handled = true; }
                return;
            }
            if (e.Key == Key.System && e.SystemKey == Key.Left) { GoBack(); e.Handled = true; return; }
            if (e.Key == Key.System && e.SystemKey == Key.Right) { GoForward(); e.Handled = true; return; }
            var sel = SelectedEntries();
            switch (e.Key)
            {
                case Key.Delete: if (sel.Count > 0) DeleteSelected(); break;
                case Key.F2: if (sel.Count == 1) Rename(sel[0].Rel, sel[0].IsFolder); break;
                case Key.Enter:
                    if (sel.Count != 1) return;
                    if (sel[0].IsFolder) Navigate(sel[0].Rel, true); else ApplyItem(sel[0]);
                    break;
                case Key.Back: GoBack(); break;
                case Key.F: if (!ctrl) return; Search.Focus(); break;
                case Key.I: if (!ctrl) return; ImportDialog(); break;
                case Key.N: if (!ctrl) return; NewFolder(currentRel); break;
                default: return;
            }
            e.Handled = true;
        }

        static T Ancestor<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T))
                d = (d is Visual || d is System.Windows.Media.Media3D.Visual3D) ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }
    }
}
