using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace CLWallpaper.Ui
{
    /// <summary>Codec warnings, loop cross-fade, day/night schedule and updates.</summary>
    sealed partial class MainView
    {
        bool codecProblem;

        void WireExtras()
        {
            SwLoopFade.Click += delegate { cur.LoopFade = SwLoopFade.IsChecked == true; CommitItem(true); };
            BtnStore.Click += delegate { OpenStore(BtnStore.Tag as string); };

            SwConvertGif.Click += delegate { settings.ConvertGif = SwConvertGif.IsChecked == true; SaveSettingsNow(); };
            SwSchedule.Click += delegate
            {
                settings.Schedule = SwSchedule.IsChecked == true;
                SaveSettingsNow();
                Ipc.NotifyHost(settings.ScheduleActive);
                if (settings.Schedule && !settings.Day.IsSet && !settings.Night.IsSet)
                    ShowToast("Choisissez les fonds : clic droit sur une vidéo, une image ou un dossier → Fond de jour / Fond de nuit", 5000);
            };
            WireTime(DayTime, false);
            WireTime(NightTime, true);

            SwUpdateCheck.Click += delegate { settings.UpdateCheck = SwUpdateCheck.IsChecked == true; SaveSettingsNow(); };
            UpdateSource.LostFocus += delegate { SaveUpdateSource(); };
            UpdateSource.KeyDown += (s, e) => { if (e.Key == Key.Enter) { SaveUpdateSource(); e.Handled = true; } };
            BtnCheckUpdate.Click += delegate { SaveUpdateSource(); CheckForUpdate(true); };
            BtnUpdateFile.Click += delegate { UpdateFromFile(); };

            Window.Loaded += delegate
            {
                if (UiApp.JustUpdated) ShowToast("CL-Wallpaper est à jour · version " + Store.Version, 4000);
                if (configured && settings.UpdateCheck && settings.UpdateSource.Trim().Length > 0)
                {
                    var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                    t.Tick += delegate { t.Stop(); CheckForUpdate(false); };
                    t.Start();
                }
            };
        }

        void LoadExtraSettings()
        {
            SwConvertGif.IsChecked = settings.ConvertGif;
            SwSchedule.IsChecked = settings.Schedule;
            DayTime.Text = Hhmm(settings.DayStart);
            NightTime.Text = Hhmm(settings.NightStart);
            DayName.Text = Describe(settings.Day, "jour");
            NightName.Text = Describe(settings.Night, "nuit");
            SwUpdateCheck.IsChecked = settings.UpdateCheck;
            UpdateSource.Text = settings.UpdateSource;
            UpdateStatus.Text = "Version " + Store.Version;
            SettingsScroll.MaxHeight = Math.Max(240, Window.ActualHeight - 190);
        }

        // ------------------------------------------------------------------ codecs

        void CheckCodec(EntryVM e)
        {
            string path = e.FullPath;
            Task.Factory.StartNew(() => CodecCache.Get(path)).ContinueWith(t =>
            {
                if (t.Exception != null || inspected != e) return;
                var ci = t.Result;
                if (ci.Problem == null) return;
                ShowPreviewError(ci.Problem);
                codecProblem = true;
                if (ci.StoreId != null) { BtnStore.Tag = ci.StoreId; BtnStore.Visibility = Visibility.Visible; }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void OfferStore(CodecInfo ci)
        {
            Prompt("Codec " + ci.Name + " manquant", "Windows ne sait pas encore lire ces vidéos. " + ci.Problem, null, "Ouvrir le Microsoft Store", false,
                _ => { OpenStore(ci.StoreId); return null; });
        }

        void OpenStore(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            try { Process.Start("ms-windows-store://pdp/?ProductId=" + id); }
            catch { try { Process.Start("https://apps.microsoft.com/detail/" + id); } catch { } }
        }

        // ------------------------------------------------------------------ day / night

        static string Hhmm(int min) { return (min / 60).ToString("00") + ":" + (min % 60).ToString("00"); }

        static bool TryParseTime(string s, out int min)
        {
            min = 0;
            s = (s ?? "").Trim().ToLowerInvariant().Replace('h', ':').Replace('.', ':');
            if (s.EndsWith(":")) s += "00";
            if (s.IndexOf(':') < 0) s += ":00";
            var p = s.Split(':');
            int h, m;
            if (p.Length != 2 || !int.TryParse(p[0], out h) || !int.TryParse(p[1], out m) || h < 0 || h > 23 || m < 0 || m > 59) return false;
            min = h * 60 + m;
            return true;
        }

        void WireTime(System.Windows.Controls.TextBox box, bool night)
        {
            Action commit = () =>
            {
                int m;
                if (!TryParseTime(box.Text, out m)) { box.Text = Hhmm(night ? settings.NightStart : settings.DayStart); ShowToast("Heure invalide : écrivez par exemple 07:30"); return; }
                box.Text = Hhmm(m);
                if ((night ? settings.NightStart : settings.DayStart) == m) return;
                if (night) settings.NightStart = m; else settings.DayStart = m;
                SaveSettingsNow();
                Ipc.NotifyHost(false);
            };
            box.LostFocus += delegate { commit(); };
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { commit(); e.Handled = true; } };
        }

        static string Describe(PlayState p, string which)
        {
            if (!p.IsSet) return "Aucun fond de " + which + " · clic droit sur un fond";
            if (p.Kind == "folder") return "Dossier « " + (p.Path.Length == 0 ? Store.LibraryName : Path.GetFileName(p.Path)) + " »";
            return Path.GetFileNameWithoutExtension(p.Path);
        }

        void SetScheduled(string kind, string rel, bool night)
        {
            FlushSave();
            var p = new PlayState { Kind = kind, Path = rel };
            if (night) settings.Night = p; else settings.Day = p;
            bool wasOn = settings.Schedule;
            settings.Schedule = true;
            SaveSettingsNow();
            if (settings.IsNight(DateTime.Now) == night)
            {
                // the new choice is for the current period: it replaces any manual choice right away
                var st = Store.LoadState();
                st.At = 0;
                Store.SaveState(st);
            }
            Ipc.NotifyHost(true);
            hostRunning = true;
            statusStamp = DateTime.MinValue;
            ShowToast((night ? "Fond de nuit" : "Fond de jour") + " · " + Describe(p, "") + " · à partir de " + Hhmm(night ? settings.NightStart : settings.DayStart)
                + (wasOn ? "" : "\nProgrammation jour / nuit activée (Réglages)"), 4000);
        }

        // ------------------------------------------------------------------ updates

        void SaveUpdateSource()
        {
            string v = UpdateSource.Text.Trim();
            if (v == settings.UpdateSource) return;
            settings.UpdateSource = v;
            SaveSettingsNow();
        }

        void CheckForUpdate(bool manual)
        {
            string src = settings.UpdateSource;
            if (src.Trim().Length == 0) { UpdateStatus.Text = "Indiquez d'abord une source."; return; }
            UpdateStatus.Text = "Recherche…";
            Task.Factory.StartNew(() => Updater.Check(src)).ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    string m = t.Exception.InnerException.Message;
                    Store.Log("update check: " + t.Exception.InnerException);
                    UpdateStatus.Text = "⚠ " + m;
                    return;
                }
                var r = t.Result;
                if (!r.IsNewer) { UpdateStatus.Text = "À jour · version " + Store.Version; if (manual) ShowToast("CL-Wallpaper est à jour"); return; }
                UpdateStatus.Text = "Version " + r.Version + " disponible";
                string notes = (r.Notes ?? "").Trim();
                if (notes.Length > 400) notes = notes.Substring(0, 400) + "…";
                Prompt("Mise à jour " + r.Version + " disponible", "Version installée : " + Store.Version + (notes.Length > 0 ? "\n\n" + notes : ""), null,
                    "Installer et redémarrer", false, _ => { DownloadAndInstall(r); return null; });
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void DownloadAndInstall(Updater.Release r)
        {
            ShowToast("Téléchargement de la version " + r.Version + "…", 120000);
            Task.Factory.StartNew(() => Updater.Download(r)).ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    Store.Log("update download: " + t.Exception.InnerException);
                    ShowToast("Mise à jour impossible : " + t.Exception.InnerException.Message, 6000);
                    return;
                }
                InstallNow(t.Result);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void InstallNow(string file)
        {
            try
            {
                FlushSave();
                StopPreview();
                Updater.Install(file);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Store.Log("update install: " + ex);
                ShowToast("Installation impossible : " + ex.Message, 6000);
            }
        }

        void UpdateFromFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Choisir une version de CL-Wallpaper", Filter = "CL-Wallpaper|*.exe" };
            if (dlg.ShowDialog(Window) != true) return;
            string file = dlg.FileName;
            if (string.Equals(Path.GetFullPath(file), Store.ExePath, StringComparison.OrdinalIgnoreCase)) { UpdateStatus.Text = "C'est la version déjà installée."; return; }
            string ver, theirs;
            try
            {
                ver = Updater.CheckIsApp(file);
                string mine = Updater.Signer(Store.ExePath);
                theirs = Updater.Signer(file);
                if (mine != null && theirs != mine)
                    throw new InvalidOperationException(theirs == null ? "ce fichier n'est pas signé, alors que l'app installée l'est." : "ce fichier est signé par un autre éditeur.");
                string staged = Path.Combine(Store.MetaDir, "update", "CL-Wallpaper-local.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(staged));
                File.Copy(file, staged, true);
                file = staged;
            }
            catch (Exception ex) { UpdateStatus.Text = "⚠ Refusé : " + ex.Message; return; }
            int cmp = Updater.Compare(ver, Store.Version);
            string what = cmp > 0 ? "Mettre à jour vers la version " + ver + " ?" : (cmp == 0 ? "Réinstaller la version " + ver + " ?" : "Revenir à la version " + ver + " ?");
            string msg = "Version installée : " + Store.Version + ". " + (theirs != null ? "Signature vérifiée." : "Ce fichier n'est pas signé : installez-le seulement s'il vient d'une source sûre.");
            string staged2 = file;
            Prompt(what, msg, null, "Installer et redémarrer", false, _ => { InstallNow(staged2); return null; });
        }
    }
}
