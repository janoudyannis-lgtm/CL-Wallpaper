using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CLWallpaper.Ui
{
    sealed partial class MainView
    {
        const string DragFormat = "CLWallpaper.Items";
        Point dragStart;
        EntryVM dragItem;
        bool dragArmed, sheetCancelable;
        Func<string, string> promptHandler;
        Action<FolderVM> moveHandler;
        bool loadingSettings;

        // ------------------------------------------------------------------ wiring

        void WireLibrary()
        {
            BtnBack.Click += delegate { GoBack(); };
            BtnFwd.Click += delegate { GoForward(); };
            BtnImport.Click += delegate { ImportDialog(); };
            BtnNewFolder.Click += delegate { NewFolder(currentRel); };
            BtnNewFolderSide.Click += delegate { NewFolder(""); };
            ZoomSlider.ValueChanged += delegate
            {
                ApplyCardSize();
                settings.CardSize = Math.Round(ZoomSlider.Value);
                settingsDirty = true;
                saveTimer.Stop(); saveTimer.Start();
            };
            Search.TextChanged += delegate { view.Refresh(); UpdateEmpty(); };

            Tree.SelectedItemChanged += (s, e) =>
            {
                if (suppressTree) return;
                var vm = e.NewValue as FolderVM;
                if (vm != null && !string.Equals(vm.Rel, currentRel, StringComparison.OrdinalIgnoreCase)) Navigate(vm.Rel, true);
            };
            Tree.PreviewMouseRightButtonUp += OnTreeRightClick;
            Tree.DragOver += OnTreeDragOver;
            Tree.DragLeave += delegate { ClearDropTargets(); };
            Tree.Drop += OnTreeDrop;

            List.SelectionChanged += delegate { OnSelection(); };
            List.MouseDoubleClick += (s, e) =>
            {
                var item = Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);
                if (item == null) return;
                var vm = (EntryVM)item.DataContext;
                if (vm.IsFolder) Navigate(vm.Rel, true); else ApplyItem(vm);
            };
            List.PreviewMouseLeftButtonDown += OnListMouseDown;
            List.PreviewMouseMove += OnListMouseMove;
            List.PreviewMouseRightButtonUp += OnListRightClick;
            DropZone.DragEnter += OnListDragOver;
            DropZone.DragOver += OnListDragOver;
            DropZone.DragLeave += delegate { ClearDropTargets(); DropOverlay.Visibility = Visibility.Collapsed; };
            DropZone.Drop += OnListDrop;
        }

        // ------------------------------------------------------------------ gallery mouse

        void OnListMouseDown(object sender, MouseButtonEventArgs e)
        {
            var src = e.OriginalSource as DependencyObject;
            var item = Ancestor<ListBoxItem>(src);
            if (item == null)
            {
                dragArmed = false;
                if (Ancestor<System.Windows.Controls.Primitives.ScrollBar>(src) == null) { List.UnselectAll(); List.Focus(); }
                return;
            }
            dragStart = e.GetPosition(List);
            dragItem = item.DataContext as EntryVM;
            dragArmed = true;
        }

        void OnListMouseMove(object sender, MouseEventArgs e)
        {
            if (!dragArmed) return;
            if (e.LeftButton != MouseButtonState.Pressed) { dragArmed = false; return; }
            var p = e.GetPosition(List);
            if (Math.Abs(p.X - dragStart.X) < 6 && Math.Abs(p.Y - dragStart.Y) < 6) return;
            dragArmed = false;
            var sel = SelectedEntries();
            if (dragItem == null) return;
            if (!sel.Contains(dragItem)) sel = new List<EntryVM> { dragItem };
            var data = new DataObject(DragFormat, sel.Select(x => x.Rel).ToArray());
            try { DragDrop.DoDragDrop(List, data, DragDropEffects.Move); }
            finally { ClearDropTargets(); DropOverlay.Visibility = Visibility.Collapsed; }
        }

        EntryVM EntryUnder(object src)
        {
            var item = Ancestor<ListBoxItem>(src as DependencyObject);
            return item == null ? null : item.DataContext as EntryVM;
        }

        void ClearDropTargets()
        {
            foreach (var e in entries) e.IsDropTarget = false;
            foreach (var f in AllFolders()) f.IsDropTarget = false;
        }

        void OnListDragOver(object sender, DragEventArgs e)
        {
            ClearDropTargets();
            var target = EntryUnder(e.OriginalSource);
            if (e.Data.GetDataPresent(DragFormat))
            {
                var rels = (string[])e.Data.GetData(DragFormat);
                if (target != null && target.IsFolder && !rels.Contains(target.Rel, StringComparer.OrdinalIgnoreCase))
                {
                    target.IsDropTarget = true;
                    e.Effects = DragDropEffects.Move;
                }
                else e.Effects = DragDropEffects.None;
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                if (target != null && target.IsFolder) { target.IsDropTarget = true; DropOverlay.Visibility = Visibility.Collapsed; }
                else
                {
                    DropText.Text = "Déposer pour importer dans « " + FolderDisplay(currentRel) + " »";
                    DropOverlay.Visibility = Visibility.Visible;
                }
            }
            else e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        void OnListDrop(object sender, DragEventArgs e)
        {
            ClearDropTargets();
            DropOverlay.Visibility = Visibility.Collapsed;
            var target = EntryUnder(e.OriginalSource);
            string dest = target != null && target.IsFolder ? target.Rel : currentRel;
            if (e.Data.GetDataPresent(DragFormat))
            {
                if (target != null && target.IsFolder) MoveItems((string[])e.Data.GetData(DragFormat), dest);
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
                Import((string[])e.Data.GetData(DataFormats.FileDrop), dest);
            e.Handled = true;
        }

        FolderVM FolderUnder(object src)
        {
            var tvi = Ancestor<TreeViewItem>(src as DependencyObject);
            return tvi == null ? null : tvi.DataContext as FolderVM;
        }

        void OnTreeDragOver(object sender, DragEventArgs e)
        {
            ClearDropTargets();
            var vm = FolderUnder(e.OriginalSource);
            e.Effects = DragDropEffects.None;
            if (vm != null)
            {
                if (e.Data.GetDataPresent(DragFormat))
                {
                    var rels = (string[])e.Data.GetData(DragFormat);
                    if (!rels.Any(r => Store.IsSameOrChild(vm.Rel, r))) e.Effects = DragDropEffects.Move;
                }
                else if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Copy;
                vm.IsDropTarget = e.Effects != DragDropEffects.None;
            }
            e.Handled = true;
        }

        void OnTreeDrop(object sender, DragEventArgs e)
        {
            ClearDropTargets();
            var vm = FolderUnder(e.OriginalSource);
            if (vm == null) return;
            if (e.Data.GetDataPresent(DragFormat)) MoveItems((string[])e.Data.GetData(DragFormat), vm.Rel);
            else if (e.Data.GetDataPresent(DataFormats.FileDrop)) Import((string[])e.Data.GetData(DataFormats.FileDrop), vm.Rel);
            e.Handled = true;
        }

        // ------------------------------------------------------------------ context menus

        static MenuItem MI(string header, string glyph, Action a)
        {
            var m = new MenuItem { Header = header, Icon = glyph };
            m.Click += delegate { a(); };
            return m;
        }

        void OnListRightClick(object sender, MouseButtonEventArgs e)
        {
            var item = Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            var cm = new ContextMenu();
            if (item != null)
            {
                var vm = (EntryVM)item.DataContext;
                if (!item.IsSelected) { List.SelectedItems.Clear(); item.IsSelected = true; }
                var sel = SelectedEntries();
                if (sel.Count == 1)
                {
                    if (vm.IsFolder)
                    {
                        cm.Items.Add(MI("Ouvrir", "\uE838", () => Navigate(vm.Rel, true)));
                        cm.Items.Add(MI("Lire ce dossier en fond d'écran", "\uE768", () => ApplyFolder(vm.Rel)));
                    }
                    else cm.Items.Add(MI("Définir comme fond d'écran", "\uE7F4", () => ApplyItem(vm)));
                    string kind = vm.IsFolder ? "folder" : "item";
                    cm.Items.Add(MI("Fond de jour", "\uE706", () => SetScheduled(kind, vm.Rel, false)));
                    cm.Items.Add(MI("Fond de nuit", "\uE708", () => SetScheduled(kind, vm.Rel, true)));
                    cm.Items.Add(new Separator());
                    cm.Items.Add(MI("Renommer…", "\uE8AC", () => Rename(vm.Rel, vm.IsFolder)));
                }
                cm.Items.Add(MI("Déplacer vers…", "\uE8DE", MoveSelected));
                if (sel.Count == 1) cm.Items.Add(MI("Afficher dans l'Explorateur", "\uEC50", () => Shell.Reveal(vm.FullPath)));
                cm.Items.Add(new Separator());
                cm.Items.Add(MI(sel.Count > 1 ? "Mettre " + sel.Count + " éléments à la corbeille" : "Mettre à la corbeille", "\uE74D", DeleteSelected));
            }
            else
            {
                List.UnselectAll();
                cm.Items.Add(MI("Importer des vidéos ou images…", "\uE898", ImportDialog));
                cm.Items.Add(MI("Nouveau dossier…", "\uE8F4", () => NewFolder(currentRel)));
                cm.Items.Add(new Separator());
                cm.Items.Add(MI("Lire ce dossier en fond d'écran", "\uE768", () => ApplyFolder(currentRel)));
                cm.Items.Add(MI("Ouvrir dans l'Explorateur", "\uEC50", () => Shell.Reveal(Store.Full(currentRel))));
            }
            cm.PlacementTarget = List;
            cm.IsOpen = true;
            e.Handled = true;
        }

        void OnTreeRightClick(object sender, MouseButtonEventArgs e)
        {
            var vm = FolderUnder(e.OriginalSource);
            if (vm == null) return;
            var cm = new ContextMenu();
            cm.Items.Add(MI("Lire en fond d'écran", "\uE768", () => ApplyFolder(vm.Rel)));
            cm.Items.Add(MI("Fond de jour", "\uE706", () => SetScheduled("folder", vm.Rel, false)));
            cm.Items.Add(MI("Fond de nuit", "\uE708", () => SetScheduled("folder", vm.Rel, true)));
            cm.Items.Add(new Separator());
            cm.Items.Add(MI("Nouveau sous-dossier…", "\uE8F4", () => NewFolder(vm.Rel)));
            if (vm.Rel.Length > 0) cm.Items.Add(MI("Renommer…", "\uE8AC", () => Rename(vm.Rel, true)));
            cm.Items.Add(MI("Ouvrir dans l'Explorateur", "\uEC50", () => Shell.Reveal(Store.Full(vm.Rel))));
            if (vm.Rel.Length > 0)
            {
                cm.Items.Add(new Separator());
                cm.Items.Add(MI("Mettre à la corbeille", "\uE74D", () => DeleteEntries(new List<string> { vm.Rel })));
            }
            cm.PlacementTarget = Tree;
            cm.IsOpen = true;
            e.Handled = true;
        }

        // ------------------------------------------------------------------ file operations

        static string ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Le nom ne peut pas être vide.";
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "Ces caractères sont interdits : \\ / : * ? \" < > |";
            if (name.Trim().TrimEnd('.').Length == 0) return "Nom invalide.";
            return null;
        }

        static string UniquePath(string dir, string fileName)
        {
            string target = Path.Combine(dir, fileName);
            if (!File.Exists(target) && !Directory.Exists(target)) return target;
            string bn = Path.GetFileNameWithoutExtension(fileName), ext = Path.GetExtension(fileName);
            for (int i = 2; ; i++)
            {
                target = Path.Combine(dir, bn + " (" + i + ")" + ext);
                if (!File.Exists(target) && !Directory.Exists(target)) return target;
            }
        }

        void NewFolder(string parentRel)
        {
            if (!Store.IsInitialized) return;
            Prompt("Nouveau dossier", null,
                "Nouveau dossier", "Créer", false, name =>
                {
                    string err = ValidateName(name);
                    if (err != null) return err;
                    string path = Path.Combine(Store.Full(parentRel), name.Trim());
                    if (Directory.Exists(path) || File.Exists(path)) return "Un élément porte déjà ce nom.";
                    try { Directory.CreateDirectory(path); }
                    catch (Exception ex) { return ex.Message; }
                    var parent = AllFolders().FirstOrDefault(f => string.Equals(f.Rel, parentRel, StringComparison.OrdinalIgnoreCase));
                    if (parent != null) parent.IsExpanded = true;
                    RefreshAll();
                    return null;
                });
        }

        void Rename(string rel, bool isFolder)
        {
            if (string.IsNullOrEmpty(rel)) return;
            string full = Store.Full(rel);
            string ext = isFolder ? "" : Path.GetExtension(full);
            string baseName = isFolder ? Path.GetFileName(full) : Path.GetFileNameWithoutExtension(full);
            Prompt("Renommer « " + baseName + " »", null, baseName, "Renommer", false, name =>
            {
                string err = ValidateName(name);
                if (err != null) return err;
                name = name.Trim();
                string newFull = Path.Combine(Path.GetDirectoryName(full), name + ext);
                if (string.Equals(newFull, full, StringComparison.Ordinal)) return null;
                bool caseOnly = string.Equals(newFull, full, StringComparison.OrdinalIgnoreCase);
                if (!caseOnly && (File.Exists(newFull) || Directory.Exists(newFull))) return "Un élément porte déjà ce nom.";
                string newRel = Store.Rel(newFull);
                string error = null;
                WithReleased(new[] { rel }, () =>
                {
                    if (isFolder)
                    {
                        if (caseOnly) { string tmp = full + ".~ren"; Directory.Move(full, tmp); Directory.Move(tmp, newFull); }
                        else Directory.Move(full, newFull);
                    }
                    else File.Move(full, newFull);
                    thumbs.Forget(full);
                }, ex => error = ex.Message, rel, newRel);
                if (error != null) return "Impossible de renommer : " + error;
                if (Store.IsSameOrChild(currentRel, rel)) currentRel = newRel + currentRel.Substring(rel.Length);
                RefreshAll();
                return null;
            });
        }

        void MoveSelected()
        {
            var sel = SelectedEntries();
            if (sel.Count == 0) return;
            var rels = sel.Select(x => x.Rel).ToList();
            MoveTitle.Text = sel.Count == 1 ? "Déplacer « " + sel[0].Name + " » vers…" : "Déplacer " + sel.Count + " éléments vers…";
            var targets = new List<FolderVM>();
            foreach (var f in AllFolders())
                if (!rels.Any(r => Store.IsSameOrChild(f.Rel, r))) targets.Add(f);
            MoveList.ItemsSource = targets;
            MoveList.SelectedItem = null;
            moveHandler = f => MoveItems(rels, f.Rel);
            OpenSheet(MovePanel, true);
        }

        void MoveItems(IList<string> rels, string destRel)
        {
            string destFull = Store.Full(destRel).TrimEnd('\\');
            var valid = rels.Where(r =>
                !string.Equals(Path.GetDirectoryName(Store.Full(r)).TrimEnd('\\'), destFull, StringComparison.OrdinalIgnoreCase) &&
                !Store.IsSameOrChild(destRel, r)).ToList();
            if (valid.Count == 0) return;
            int moved = 0;
            string error = null;
            foreach (var r in valid)
            {
                string src = Store.Full(r);
                string target = UniquePath(destFull, Path.GetFileName(src));
                string newRel = Store.Rel(target);
                WithReleased(new[] { r }, () =>
                {
                    if (Directory.Exists(src)) Directory.Move(src, target); else File.Move(src, target);
                    thumbs.Forget(src);
                    moved++;
                }, ex => error = ex.Message, r, newRel);
            }
            RefreshAll();
            if (error != null) ShowToast("Déplacement impossible : " + error);
            else ShowToast(moved + (moved > 1 ? " éléments déplacés" : " élément déplacé") + " vers « " + FolderDisplay(destRel) + " »");
        }

        void DeleteSelected()
        {
            var sel = SelectedEntries();
            if (sel.Count > 0) DeleteEntries(sel.Select(x => x.Rel).ToList());
        }

        void DeleteEntries(List<string> rels)
        {
            string what = rels.Count == 1 ? "« " + Path.GetFileName(rels[0]) + " »" : rels.Count + " éléments";
            Prompt("Mettre " + what + " à la corbeille ?", "Vous pourrez les restaurer depuis la Corbeille de Windows.", null,
                "Mettre à la corbeille", true, _ =>
                {
                    string error = null;
                    WithReleased(rels, () =>
                    {
                        foreach (var r in rels)
                        {
                            string full = Store.Full(r);
                            thumbs.Forget(full);
                            Retry(() => Shell.Recycle(full));
                            lib.Remove(r);
                        }
                    }, ex => error = ex.Message, null, null);
                    if (rels.Any(r => Store.IsSameOrChild(currentRel, r))) currentRel = Path.GetDirectoryName(rels[0]) ?? "";
                    RefreshAll();
                    if (error != null) return "Impossible de supprimer : " + error;
                    ShowToast(rels.Count > 1 ? rels.Count + " éléments mis à la corbeille" : "Élément mis à la corbeille");
                    return null;
                });
        }

        static void Retry(Action a)
        {
            for (int i = 0; ; i++)
            {
                try { a(); return; }
                catch (IOException) { if (i >= 8) throw; System.Threading.Thread.Sleep(150); }
            }
        }

        /// <summary>
        /// Runs a file operation on library paths, first releasing them if the wallpaper or the preview is using them,
        /// then fixes the saved settings/state (rename/move when newRel != null, deletion otherwise) and restarts the wallpaper.
        /// </summary>
        void WithReleased(IList<string> rels, Action op, Action<Exception> onError, string oldRel, string newRel)
        {
            if (PreviewPlayer != null && PreviewPlayer.FilePath != null && rels.Any(r => Store.IsSameOrChild(Store.Rel(PreviewPlayer.FilePath), r)))
                StopPreview();
            FlushSave();
            var prev = Store.LoadState();
            bool running = Ipc.HostRunning;
            bool touches = running && prev.Kind != "none" &&
                ((activeRel != null && rels.Any(r => Store.IsSameOrChild(activeRel, r))) || rels.Any(r => Store.IsSameOrChild(prev.Path, r)));
            if (touches) StopHostAndWait();
            bool ok = false;
            try { op(); ok = true; }
            catch (Exception ex) { onError(ex); }

            bool stateDeleted = prev.Kind != "none" && rels.Any(r => Store.IsSameOrChild(prev.Path, r));
            if (ok && newRel != null)
            {
                lib.Rename(oldRel, newRel);
                if (prev.Kind != "none" && Store.IsSameOrChild(prev.Path, oldRel)) prev.Path = newRel + prev.Path.Substring(oldRel.Length);
                stateDeleted = false;
            }
            Store.SaveLibrary(lib);
            if (ok && newRel == null && stateDeleted) { prev.Kind = "none"; Store.SaveState(prev); }
            else if (prev.Kind != "none") Store.SaveState(prev);
            if (touches && prev.Kind != "none") Ipc.NotifyHost(true);
            else if (running && prev.Kind != "none") Ipc.NotifyHost(false);
            statusStamp = DateTime.MinValue;
        }

        // ------------------------------------------------------------------ import

        void ImportDialog()
        {
            if (!Store.IsInitialized) return;
            string vids = string.Join(";", Store.VideoExt.Select(x => "*" + x));
            string imgs = string.Join(";", Store.ImageExt.Select(x => "*" + x));
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Importer des fonds d'écran dans « " + FolderDisplay(currentRel) + " »",
                Multiselect = true,
                Filter = "Vidéos et images|" + vids + ";" + imgs + "|Vidéos|" + vids + "|Images|" + imgs + "|Tous les fichiers|*.*"
            };
            if (dlg.ShowDialog(Window) == true) Import(dlg.FileNames, currentRel);
        }

        void Import(string[] paths, string destRel)
        {
            if (!Store.IsInitialized || paths == null) return;
            string destDir = Store.Full(destRel);
            var jobs = new List<KeyValuePair<string, string>>();
            foreach (var p in paths)
            {
                if (Directory.Exists(p))
                {
                    string root = Path.Combine(destDir, Path.GetFileName(p.TrimEnd('\\')));
                    foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories).Where(Store.IsMedia))
                    {
                        string relDir = Path.GetDirectoryName(f).Substring(p.TrimEnd('\\').Length).TrimStart('\\');
                        jobs.Add(new KeyValuePair<string, string>(f, Path.Combine(root, relDir)));
                    }
                }
                else if (File.Exists(p) && Store.IsMedia(p)) jobs.Add(new KeyValuePair<string, string>(p, destDir));
            }
            if (jobs.Count == 0) { ShowToast("Aucun fichier vidéo ou image compatible"); return; }
            ShowToast(jobs.Count > 1 ? "Importation de " + jobs.Count + " fichiers…" : "Importation…");
            int done = 0, converted = 0;
            bool convertGif = settings.ConvertGif;
            var missing = new Dictionary<string, CodecInfo>(); // codec name → info, for files Windows cannot decode
            Task.Factory.StartNew(() =>
            {
                foreach (var j in jobs)
                {
                    Directory.CreateDirectory(j.Value);
                    if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(j.Key)).TrimEnd('\\'), j.Value.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                    if (convertGif && MfTools.IsAnimatedGif(j.Key))
                    {
                        string mp4 = UniquePath(j.Value, Path.GetFileNameWithoutExtension(j.Key) + ".mp4");
                        try { MfTools.GifToMp4(j.Key, mp4); converted++; done++; continue; }
                        catch (Exception ex) { Store.Log("gif → mp4 " + j.Key + ": " + ex.Message); } // keep the GIF
                    }
                    string dest = UniquePath(j.Value, Path.GetFileName(j.Key));
                    File.Copy(j.Key, dest);
                    done++;
                    if (Store.IsVideo(dest))
                    {
                        var ci = CodecCache.Get(dest);
                        if (ci.Problem != null && !missing.ContainsKey(ci.Name)) missing[ci.Name] = ci;
                    }
                }
            }).ContinueWith(t =>
            {
                if (t.Exception != null) Store.Log("import: " + t.Exception.InnerException);
                RefreshAll();
                if (t.Exception != null) { ShowToast("Erreur d'importation : " + t.Exception.InnerException.Message); return; }
                string msg = done + (done > 1 ? " fonds importés" : " fond importé");
                if (converted > 0) msg += converted > 1 ? " · " + converted + " GIF convertis en MP4" : " · GIF converti en MP4";
                if (missing.Count > 0)
                {
                    var ci = missing.Values.First();
                    if (ci.StoreId != null) OfferStore(ci); else msg += "\n⚠ " + ci.Problem;
                }
                ShowToast(msg, missing.Count > 0 ? 9000 : 2600);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ------------------------------------------------------------------ sheets

        void WireSheets()
        {
            SheetLayer.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource == SheetLayer && sheetCancelable) CloseSheet(); };
            BtnPromptCancel.Click += delegate { CloseSheet(); };
            BtnPromptOk.Click += delegate { PromptOk(); };
            BtnMoveCancel.Click += delegate { CloseSheet(); };
            BtnMoveOk.Click += delegate { MoveOk(); };
            MoveList.MouseDoubleClick += delegate { MoveOk(); };

            BtnSetupBrowse.Click += delegate
            {
                string start = Directory.Exists(SetupPath.Text) ? SetupPath.Text : Path.GetDirectoryName(SetupPath.Text);
                string p = Shell.PickFolder(Window, "Où créer le répertoire de CL-Wallpaper ?", start);
                if (p == null) return;
                bool empty = !Directory.EnumerateFileSystemEntries(p).Any();
                SetupPath.Text = empty || string.Equals(Path.GetFileName(p), Store.AppName, StringComparison.OrdinalIgnoreCase) ? p : Path.Combine(p, Store.AppName);
            };
            BtnSetupGo.Click += delegate { FinishSetup(); };

            BtnSettingsDone.Click += delegate { CloseSheet(); };
            BtnOpenData.Click += delegate { Shell.Reveal(Store.DataDir); };
            segTheme.Changed += i => { if (loadingSettings) return; settings.Theme = i; SaveSettingsNow(); ApplyTheme(); };
            segStyle.Changed += i => { if (loadingSettings) return; settings.Style = i; SaveSettingsNow(); ApplyTheme(); };
            segMonitors.Changed += i => { if (loadingSettings) return; settings.Monitors = i; SaveSettingsNow(); Ipc.NotifyHost(false); };
            SwAutostart.Click += delegate
            {
                try { Autostart.Set(SwAutostart.IsChecked == true); }
                catch (Exception ex) { ShowToast("Impossible : " + ex.Message); SwAutostart.IsChecked = Autostart.Enabled; }
            };
            SwMute.Click += delegate { settings.Mute = SwMute.IsChecked == true; SaveSettingsNow(); Ipc.NotifyHost(false); };
            SwFade.Click += delegate { settings.Fade = SwFade.IsChecked == true; SaveSettingsNow(); Ipc.NotifyHost(false); };
            SwFullscreen.Click += delegate { settings.PauseFullscreen = SwFullscreen.IsChecked == true; SaveSettingsNow(); Ipc.NotifyHost(false); };
            SwMaximized.Click += delegate { settings.PauseMaximized = SwMaximized.IsChecked == true; SaveSettingsNow(); Ipc.NotifyHost(false); };
            SwBattery.Click += delegate { settings.PauseBattery = SwBattery.IsChecked == true; SaveSettingsNow(); Ipc.NotifyHost(false); };
        }

        void OpenSheet(StackPanel panel, bool cancelable)
        {
            foreach (var p in new[] { SetupPanel, SettingsPanel, PromptPanel, MovePanel })
                p.Visibility = p == panel ? Visibility.Visible : Visibility.Collapsed;
            sheetCancelable = cancelable;
            if (preview != null) preview.Visibility = Visibility.Collapsed; // native window would float above the sheet
            SheetLayer.Visibility = Visibility.Visible;
            SheetLayer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            var tt = SheetCard.RenderTransform as TranslateTransform;
            if (tt != null) tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-14, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        void CloseSheet()
        {
            SheetLayer.Visibility = Visibility.Collapsed;
            promptHandler = null;
            moveHandler = null;
            if (preview != null && inspected != null && inspected.IsVideo && PreviewPlayer != null && PreviewPlayer.FilePath != null)
                preview.Visibility = Visibility.Visible;
        }

        void Prompt(string title, string msg, string input, string ok, bool danger, Func<string, string> onOk)
        {
            PromptTitle.Text = title;
            PromptMsg.Text = msg ?? "";
            PromptMsg.Visibility = string.IsNullOrEmpty(msg) ? Visibility.Collapsed : Visibility.Visible;
            PromptInput.Visibility = input == null ? Visibility.Collapsed : Visibility.Visible;
            PromptInput.Text = input ?? "";
            PromptError.Visibility = Visibility.Collapsed;
            PromptCheck.Visibility = Visibility.Collapsed;
            BtnPromptOk.Content = ok;
            BtnPromptOk.SetResourceReference(FrameworkElement.StyleProperty, danger ? "DangerBtn" : "PrimaryBtnSmall");
            promptHandler = onOk;
            OpenSheet(PromptPanel, true);
            if (input != null)
                Window.Dispatcher.BeginInvoke(new Action(() => { PromptInput.Focus(); PromptInput.SelectAll(); }), DispatcherPriority.Input);
        }

        void PromptOk()
        {
            var h = promptHandler;
            string err = h == null ? null : h(PromptInput.Text);
            if (err != null)
            {
                PromptError.Text = err;
                PromptError.Visibility = Visibility.Visible;
                return;
            }
            if (promptHandler == h) CloseSheet();
        }

        void MoveOk()
        {
            var f = MoveList.SelectedItem as FolderVM;
            if (f == null) return;
            var h = moveHandler;
            CloseSheet();
            if (h != null) h(f);
        }

        void ShowSettings()
        {
            if (!Store.IsInitialized) return;
            loadingSettings = true;
            segTheme.Selected = settings.Theme;
            segStyle.Selected = settings.Style;
            segMonitors.Selected = settings.Monitors;
            SwAutostart.IsChecked = Autostart.Enabled;
            SwMute.IsChecked = settings.Mute;
            SwFade.IsChecked = settings.Fade;
            LoadExtraSettings();
            SwFullscreen.IsChecked = settings.PauseFullscreen;
            SwMaximized.IsChecked = settings.PauseMaximized;
            SwBattery.IsChecked = settings.PauseBattery;
            DataPathText.Text = Store.DataDir;
            DataPathText.ToolTip = Store.DataDir;
            loadingSettings = false;
            OpenSheet(SettingsPanel, true);
        }

        // ------------------------------------------------------------------ first run

        void ShowSetup()
        {
            SetupPath.Text = Store.SuggestedDir;
            ChkInstall.IsChecked = !Store.DataForced;
            ChkStartMenu.IsChecked = !Store.DataForced;
            ChkDesktop.IsChecked = false;
            SetupError.Visibility = Visibility.Collapsed;
            OpenSheet(SetupPanel, false);
        }

        void FinishSetup()
        {
            string dir = (SetupPath.Text ?? "").Trim().Trim('"');
            SetupError.Visibility = Visibility.Collapsed;
            if (dir.Length < 3 || !Path.IsPathRooted(dir))
            {
                SetupError.Text = "Indiquez un chemin complet, par exemple C:\\Users\\vous\\CL-Wallpaper.";
                SetupError.Visibility = Visibility.Visible;
                return;
            }
            try
            {
                Store.SetDir(dir);
                Store.CreateStructure();
                Store.SaveSettings(settings);
                if (!Store.DataForced) Store.WritePointer(Store.DataDir);

                string exe = Store.ExePath;
                string installed = Path.Combine(Store.DataDir, Store.AppName + ".exe");
                bool relaunch = false;
                if (ChkInstall.IsChecked == true && !string.Equals(exe, installed, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(exe, installed, true);
                    exe = installed;
                    relaunch = true;
                }
                if (ChkStartMenu.IsChecked == true)
                    Shell.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Store.AppName + ".lnk"), exe, "", "Fonds d'écran animés");
                if (ChkDesktop.IsChecked == true)
                    Shell.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Store.AppName + ".lnk"), exe, "", "Fonds d'écran animés");

                if (relaunch)
                {
                    UiApp.ReleaseInstance();
                    Process.Start(new ProcessStartInfo(exe, Store.DataArg.Trim()) { UseShellExecute = false });
                    Application.Current.Shutdown();
                    return;
                }
                configured = true;
                CloseSheet();
                InitLibrary();
                ShowToast("Répertoire créé · " + Store.DataDir);
            }
            catch (Exception ex)
            {
                SetupError.Text = "Impossible de créer le répertoire : " + ex.Message;
                SetupError.Visibility = Visibility.Visible;
            }
        }
    }
}
