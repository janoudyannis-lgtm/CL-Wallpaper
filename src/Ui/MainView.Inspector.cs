using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CLWallpaper.Ui
{
    sealed partial class MainView
    {
        static readonly double[] Speeds = { 0.5, 0.75, 1, 1.5, 2 };
        static readonly int[] LoopCounts = { 1, 2, 3, 5, 10 };
        static readonly double[] ImageDurations = { 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600 };

        EntryVM inspected;
        string inspectedFolder = "";
        bool loadingInsp;
        ItemSettings cur = new ItemSettings();
        FolderSettings curFolder = new FolderSettings();
        VideoHost preview;
        EntryVM pendingPreview;

        VideoPlayer PreviewPlayer { get { return preview != null ? preview.Player : null; } }

        void WireInspector()
        {
            BtnApply.Click += delegate { if (inspected != null) ApplyItem(inspected); };
            BtnReset.Click += delegate
            {
                if (inspected == null) return;
                cur = new ItemSettings();
                lib.SetItem(inspected.Rel, cur);
                if (PreviewPlayer != null && PreviewPlayer.FilePath != null) PreviewPlayer.Update(cur);
                MarkDirty(AffectsActive(inspected.Rel));
                ShowItemInspector(inspected);
            };
            segFit.Changed += i => { cur.Fit = i; UpdatePreviewStretch(); CommitItem(true); };
            segSpeed.Changed += i => { cur.Speed = Speeds[i]; CommitItem(true); };
            segLoop.Changed += i =>
            {
                cur.Loop = i;
                PausePanel.Visibility = i == 2 ? Visibility.Visible : Visibility.Collapsed;
                LoopFadeBox.Visibility = i == 1 ? Visibility.Collapsed : Visibility.Visible;
                CommitItem(true);
            };
            PauseSlider.ValueChanged += delegate
            {
                PauseText.Text = Fmt.Seconds(PauseSlider.Value);
                if (loadingInsp) return;
                cur.PauseSec = PauseSlider.Value;
                CommitItem(true);
            };
            VolSlider.ValueChanged += delegate
            {
                UpdateVolText();
                if (loadingInsp) return;
                cur.Volume = Math.Round(VolSlider.Value) / 100.0;
                CommitItem(false);
            };
            trim.Scrub += v => { if (PreviewPlayer != null) PreviewPlayer.Scrub(v); };
            trim.Changed += final =>
            {
                if (loadingInsp || inspected == null) return;
                cur.Start = trim.Lower < 0.05 ? 0 : Math.Round(trim.Lower, 2);
                cur.End = trim.Upper >= trim.Maximum - 0.05 ? 0 : Math.Round(trim.Upper, 2);
                UpdateTrimText();
                if (final)
                {
                    if (PreviewPlayer != null) PreviewPlayer.EndScrub();
                    CommitItem(true);
                }
            };

            BtnPlayFolder.Click += delegate { ApplyFolder(inspectedFolder); };
            segOrder.Changed += i => { curFolder.Order = i; CommitFolder(); };
            SwRecursive.Click += delegate { curFolder.Recursive = SwRecursive.IsChecked == true; CommitFolder(); UpdateFolderSub(); };
            ImgDurSlider.ValueChanged += delegate
            {
                int i = (int)Math.Round(ImgDurSlider.Value);
                ImgDurText.Text = Fmt.Seconds(ImageDurations[Math.Max(0, Math.Min(ImageDurations.Length - 1, i))]);
                if (loadingInsp) return;
                curFolder.ImageSec = ImageDurations[i];
                CommitFolder();
            };
            segLoops.Changed += i => { curFolder.VideoLoops = LoopCounts[i]; CommitFolder(); };
            BtnMultiMove.Click += delegate { MoveSelected(); };
            BtnMultiDelete.Click += delegate { DeleteSelected(); };
        }

        void CommitItem(bool updatePreview)
        {
            if (loadingInsp || inspected == null) return;
            lib.SetItem(inspected.Rel, cur);
            if (updatePreview && PreviewPlayer != null && PreviewPlayer.FilePath != null)
            {
                try { PreviewPlayer.Update(cur); } catch (Exception ex) { Store.Log("preview update: " + ex.Message); }
            }
            MarkDirty(AffectsActive(inspected.Rel));
        }

        void CommitFolder()
        {
            if (loadingInsp) return;
            lib.SetFolder(inspectedFolder, curFolder);
            MarkDirty(hostRunning && activeKind == "folder" && string.Equals(activePath, inspectedFolder, StringComparison.OrdinalIgnoreCase));
        }

        // ------------------------------------------------------------------ panels

        void ShowPanel(string which)
        {
            InspItem.Visibility = which == "item" ? Visibility.Visible : Visibility.Collapsed;
            InspFolder.Visibility = which == "folder" ? Visibility.Visible : Visibility.Collapsed;
            InspMulti.Visibility = which == "multi" ? Visibility.Visible : Visibility.Collapsed;
            if (which != "item") { inspected = null; StopPreview(); }
        }

        void OnSelection()
        {
            var sel = SelectedEntries();
            if (sel.Count == 0) ShowFolderInspector(currentRel);
            else if (sel.Count == 1)
            {
                if (sel[0].IsFolder) ShowFolderInspector(sel[0].Rel);
                else ShowItemInspector(sel[0]);
            }
            else
            {
                ShowPanel("multi");
                MultiText.Text = sel.Count + " éléments sélectionnés";
            }
        }

        EntryVM watched;

        void OnInspectedChanged(object sender, System.ComponentModel.PropertyChangedEventArgs a)
        {
            if (sender != inspected) return;
            if (a.PropertyName == "Thumb") UpdatePreviewImage(inspected.Thumb);
            else if (a.PropertyName == "Sub")
            {
                InspSub.Text = Details(inspected);
                if (inspected.IsVideo && inspected.Duration > 0 && Math.Abs(trim.Maximum - inspected.Duration) > 0.2)
                {
                    loadingInsp = true;
                    trim.Set(0, inspected.Duration, cur.Start, cur.End > 0 ? cur.End : inspected.Duration);
                    UpdateTrimText();
                    loadingInsp = false;
                }
            }
        }

        void ShowItemInspector(EntryVM e)
        {
            ShowPanel("item");
            if (watched != null) watched.PropertyChanged -= OnInspectedChanged;
            watched = e;
            e.PropertyChanged += OnInspectedChanged;
            inspected = e;
            loadingInsp = true;
            try
            {
                cur = lib.Item(e.Rel);
                InspName.Text = e.Name;
                InspSub.Text = Details(e);
                PreviewError.Visibility = Visibility.Collapsed;
                BtnStore.Visibility = Visibility.Collapsed;
                codecProblem = false;
                UpdatePreviewImage(e.Thumb);
                segFit.Selected = cur.Fit;
                VideoOpts.Visibility = e.IsVideo ? Visibility.Visible : Visibility.Collapsed;
                if (e.IsVideo)
                {
                    double dur = e.Duration > 0 ? e.Duration : Math.Max(cur.End, 1);
                    trim.Set(0, dur, cur.Start, cur.End > 0 ? cur.End : dur);
                    UpdateTrimText();
                    segSpeed.Selected = Closest(Speeds, cur.Speed);
                    segLoop.Selected = cur.Loop;
                    PausePanel.Visibility = cur.Loop == 2 ? Visibility.Visible : Visibility.Collapsed;
                    LoopFadeBox.Visibility = cur.Loop == 1 ? Visibility.Collapsed : Visibility.Visible;
                    SwLoopFade.IsChecked = cur.LoopFade;
                    CheckCodec(e);
                    PauseSlider.Value = Math.Max(1, Math.Min(60, cur.PauseSec));
                    PauseText.Text = Fmt.Seconds(PauseSlider.Value);
                    VolSlider.Value = Math.Round(cur.Volume * 100);
                    UpdateVolText();
                    StartPreview(e);
                }
                else StopPreview();
                UpdatePreviewStretch();
                RefreshInspectorButtons();
            }
            finally { loadingInsp = false; }
        }

        void ShowFolderInspector(string rel)
        {
            ShowPanel("folder");
            inspectedFolder = rel ?? "";
            loadingInsp = true;
            try
            {
                curFolder = lib.Folder(inspectedFolder);
                FolderName.Text = FolderDisplay(inspectedFolder);
                UpdateFolderSub();
                segOrder.Selected = curFolder.Order;
                SwRecursive.IsChecked = curFolder.Recursive;
                ImgDurSlider.Value = Closest(ImageDurations, curFolder.ImageSec);
                ImgDurText.Text = Fmt.Seconds(curFolder.ImageSec);
                segLoops.Selected = Array.IndexOf(LoopCounts, curFolder.VideoLoops) >= 0 ? Array.IndexOf(LoopCounts, curFolder.VideoLoops) : 0;
                RefreshInspectorButtons();
            }
            finally { loadingInsp = false; }
        }

        void UpdateFolderSub()
        {
            if (!Store.IsInitialized) { FolderSub.Text = ""; return; }
            var media = Store.SortedMedia(Store.Full(inspectedFolder), curFolder.Recursive);
            int v = media.Count(Store.IsVideo), i = media.Count - v;
            if (media.Count == 0) { FolderSub.Text = "Aucun fond pour l'instant"; return; }
            var parts = new List<string>();
            if (v > 0) parts.Add(v + (v > 1 ? " vidéos" : " vidéo"));
            if (i > 0) parts.Add(i + (i > 1 ? " images" : " image"));
            FolderSub.Text = string.Join(" · ", parts);
        }

        void RefreshInspectorButtons()
        {
            if (inspected != null)
            {
                bool a = inspected.IsActive;
                BtnApply.SetResourceReference(FrameworkElement.StyleProperty, a ? "ActiveBtn" : "PrimaryBtn");
                ApplyText.Text = a ? "Fond d'écran actif" : "Définir comme fond d'écran";
                ApplyGlyph.Text = a ? "" : "";
            }
            bool fa = hostRunning && activeKind == "folder" && string.Equals(activePath, inspectedFolder, StringComparison.OrdinalIgnoreCase);
            BtnPlayFolder.SetResourceReference(FrameworkElement.StyleProperty, fa ? "ActiveBtn" : "PrimaryBtn");
            PlayFolderText.Text = fa ? "Dossier en lecture" : "Lire ce dossier en fond d'écran";
            PlayFolderGlyph.Text = fa ? "" : "";
        }

        static string Details(EntryVM e)
        {
            var parts = new List<string>();
            parts.Add((Path.GetExtension(e.FullPath) ?? "").TrimStart('.').ToUpperInvariant());
            if (e.PixelW > 0) parts.Add(e.PixelW + "×" + e.PixelH);
            if (e.IsVideo && e.Duration > 0) parts.Add(Fmt.Clock(e.Duration));
            parts.Add(Fmt.Size(e.Size));
            return string.Join("  ·  ", parts);
        }

        static int Closest(double[] arr, double v)
        {
            int best = 0;
            for (int i = 1; i < arr.Length; i++) if (Math.Abs(arr[i] - v) < Math.Abs(arr[best] - v)) best = i;
            return best;
        }

        void UpdateTrimText()
        {
            double end = trim.Upper, start = trim.Lower;
            TrimStartText.Text = "Début " + Fmt.Precise(start);
            TrimEndText.Text = "Fin " + Fmt.Precise(end);
            bool full = start < 0.05 && end >= trim.Maximum - 0.05;
            TrimInfo.Text = full ? "Vidéo entière · " + Fmt.Seconds(Math.Round(end - start, 1)) : "Extrait de " + Fmt.Seconds(Math.Round(end - start, 1));
        }

        void UpdateVolText()
        {
            int v = (int)Math.Round(VolSlider.Value);
            VolText.Text = v == 0 ? "Muet" : v + " %";
            VolIcon.Text = v == 0 ? "" : (v < 50 ? "" : "");
        }

        // ------------------------------------------------------------------ live preview

        void UpdatePreviewImage(ImageSource img)
        {
            PreviewImage.Background = img == null ? null : new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            UpdatePreviewStretch();
        }

        void UpdatePreviewStretch()
        {
            var b = PreviewImage.Background as ImageBrush;
            if (b == null) return;
            b.Stretch = cur.Fit == 1 ? Stretch.Uniform : (cur.Fit == 2 ? Stretch.Fill : Stretch.UniformToFill);
        }

        void EnsurePreviewHost()
        {
            if (preview != null) return;
            preview = new VideoHost();
            preview.Loaded += delegate
            {
                if (PreviewPlayer != null)
                {
                    PreviewPlayer.Loaded += OnPreviewLoaded;
                    PreviewPlayer.Failed += msg => Window.Dispatcher.BeginInvoke(new Action(() => ShowPreviewError(msg)));
                }
                if (pendingPreview != null) { var p = pendingPreview; pendingPreview = null; if (p == inspected) StartPreview(p); }
            };
            PreviewSlot.Content = preview;
        }

        void OnPreviewLoaded()
        {
            var p = PreviewPlayer;
            if (p == null || inspected == null || !inspected.IsVideo) return;
            if (p.Duration > 0 && Math.Abs(trim.Maximum - p.Duration) > 0.2)
            {
                loadingInsp = true;
                trim.Set(0, p.Duration, cur.Start, cur.End > 0 ? cur.End : p.Duration);
                UpdateTrimText();
                loadingInsp = false;
            }
        }

        void StartPreview(EntryVM e)
        {
            if (!inspectorVisible) return;
            EnsurePreviewHost();
            var p = PreviewPlayer;
            if (p == null) { pendingPreview = e; return; }
            if (SheetLayer.Visibility != Visibility.Visible) preview.Visibility = Visibility.Visible;
            try
            {
                if (!string.Equals(p.FilePath, e.FullPath, StringComparison.OrdinalIgnoreCase)) p.Open(e.FullPath, cur);
                else p.Update(cur);
                p.SetPaused(Window.WindowState == WindowState.Minimized);
                previewTimer.Start();
            }
            catch (Exception ex)
            {
                Store.Log("preview open: " + ex.Message);
                ShowPreviewError(VideoPlayer.Describe(ex.HResult));
            }
        }

        void ShowPreviewError(string msg)
        {
            if (preview != null) preview.Visibility = Visibility.Collapsed;
            if (inspected == null || codecProblem) return; // the codec diagnosis is more useful than the engine's error
            PreviewError.Text = "⚠ " + msg;
            PreviewError.Visibility = Visibility.Visible;
        }

        void StopPreview()
        {
            previewTimer.Stop();
            pendingPreview = null;
            if (PreviewPlayer != null) { try { PreviewPlayer.Close(); } catch { } }
            if (preview != null) preview.Visibility = Visibility.Collapsed;
        }
    }
}
