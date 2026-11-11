using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace CLWallpaper.Ui
{
    /// <summary>
    /// Built-in updater. The source is chosen by the user (nothing is contacted otherwise):
    ///  • an https URL (or file path) of a JSON manifest: {"version":"1.3.0","url":"https://…/CL-Wallpaper.exe","sha256":"…","notes":"…"}
    ///  • "github:owner/repo": the latest GitHub release with a .exe asset (its SHA-256 digest is checked).
    /// A download is installed only if its SHA-256 matches, and — when the running exe is signed — if it is signed
    /// by the same certificate. The running exe is renamed to .old, the new one takes its place, and both processes restart.
    /// </summary>
    static class Updater
    {
        public sealed class Release
        {
            public string Version = "", Url = "", Sha256 = "", Notes = "";
            public bool IsNewer { get { return Compare(Version, Store.Version) > 0; } }
        }

        public static int Compare(string a, string b)
        {
            Version va, vb;
            if (!System.Version.TryParse(Clean(a), out va)) return -1;
            if (!System.Version.TryParse(Clean(b), out vb)) return 1;
            return va.CompareTo(vb);
        }
        static string Clean(string v)
        {
            v = (v ?? "").Trim().TrimStart('v', 'V');
            int dash = v.IndexOfAny(new[] { '-', '+', ' ' });
            if (dash > 0) v = v.Substring(0, dash);
            if (v.IndexOf('.') < 0) v += ".0";
            return v;
        }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            var wc = new WebClient { Encoding = Encoding.UTF8 };
            wc.Headers[HttpRequestHeader.UserAgent] = "CL-Wallpaper-Updater";
            return wc;
        }

        static void CheckScheme(Uri u)
        {
            if (u.IsFile || u.Scheme == Uri.UriSchemeHttps) return;
            if (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback) return;
            throw new InvalidOperationException("Seules les adresses https:// (ou un fichier local) sont acceptées.");
        }

        /// <summary>Reads the source and returns the latest release (network call: run off the UI thread).</summary>
        public static Release Check(string source)
        {
            source = (source ?? "").Trim();
            if (source.Length == 0) throw new InvalidOperationException("Indiquez d'abord une source de mises à jour.");
            var r = new Release();
            using (var wc = Client())
            {
                if (source.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
                {
                    string repo = source.Substring(7).Trim().Trim('/');
                    if (repo.Split('/').Length != 2) throw new InvalidOperationException("Format attendu : github:propriétaire/dépôt");
                    wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    var d = Json.Parse(wc.DownloadString("https://api.github.com/repos/" + repo + "/releases/latest")) as IDictionary<string, object>;
                    r.Version = Json.S(d, "tag_name", "");
                    r.Notes = Json.S(d, "body", "");
                    object assets;
                    if (d != null && d.TryGetValue("assets", out assets) && assets is List<object>)
                        foreach (var o in (List<object>)assets)
                        {
                            var a = o as IDictionary<string, object>;
                            if (a == null || !Json.S(a, "name", "").EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                            r.Url = Json.S(a, "browser_download_url", "");
                            string dg = Json.S(a, "digest", "");
                            if (dg.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) r.Sha256 = dg.Substring(7);
                            break;
                        }
                    if (r.Url.Length == 0) throw new InvalidOperationException("La dernière version publiée ne contient pas de fichier .exe.");
                }
                else
                {
                    Uri u = File.Exists(source) ? new Uri(Path.GetFullPath(source)) : new Uri(source);
                    CheckScheme(u);
                    var d = Json.Parse(wc.DownloadString(u)) as IDictionary<string, object>;
                    if (d == null) throw new InvalidOperationException("Le manifeste n'est pas un JSON valide.");
                    r.Version = Json.S(d, "version", "");
                    r.Sha256 = Json.S(d, "sha256", "");
                    r.Notes = Json.S(d, "notes", "");
                    string url = Json.S(d, "url", "");
                    if (url.Length == 0) throw new InvalidOperationException("Le manifeste ne contient pas d'adresse (« url »).");
                    r.Url = new Uri(u, url).ToString(); // relative to the manifest
                }
            }
            if (Clean(r.Version).Length < 3) throw new InvalidOperationException("Numéro de version illisible : « " + r.Version + " ».");
            return r;
        }

        static string UpdateDir { get { return Path.Combine(Store.MetaDir, "update"); } }

        /// <summary>Downloads and verifies the release. Returns the path of the verified exe.</summary>
        public static string Download(Release r)
        {
            var u = new Uri(r.Url);
            CheckScheme(u);
            Directory.CreateDirectory(UpdateDir);
            string file = Path.Combine(UpdateDir, "CL-Wallpaper-" + Clean(r.Version) + ".exe");
            if (File.Exists(file)) File.Delete(file);
            using (var wc = Client()) wc.DownloadFile(u, file);
            try { Verify(file, r.Sha256); }
            catch { try { File.Delete(file); } catch { } throw; }
            return file;
        }

        /// <summary>Checks integrity (SHA-256) and origin (same signing certificate as the running exe).</summary>
        public static void Verify(string file, string sha256)
        {
            if (!string.IsNullOrEmpty(sha256))
            {
                string actual;
                using (var s = File.OpenRead(file)) using (var h = SHA256.Create())
                    actual = BitConverter.ToString(h.ComputeHash(s)).Replace("-", "");
                if (!string.Equals(actual, sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Fichier corrompu ou modifié : l'empreinte SHA-256 ne correspond pas.");
            }
            string mine = Signer(Store.ExePath), theirs = Signer(file);
            if (mine != null)
            {
                if (theirs == null) throw new InvalidOperationException("La mise à jour n'est pas signée (ou sa signature est invalide), alors que l'app installée l'est.");
                if (theirs != mine) throw new InvalidOperationException("La mise à jour est signée par un autre éditeur : installation refusée.");
            }
            else if (string.IsNullOrEmpty(sha256) && theirs == null)
                throw new InvalidOperationException("Impossible de vérifier ce fichier : ni empreinte SHA-256 ni signature.");
            CheckIsApp(file);
        }

        /// <summary>Refuses anything that is not a CL-Wallpaper build.</summary>
        public static string CheckIsApp(string file)
        {
            var vi = FileVersionInfo.GetVersionInfo(file);
            if (!string.Equals(vi.ProductName, Store.AppName, StringComparison.Ordinal))
                throw new InvalidOperationException("Ce fichier n'est pas une version de CL-Wallpaper.");
            return vi.ProductVersion ?? vi.FileVersion ?? "";
        }

        // ------------------------------------------------------------------ Authenticode

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_FILE_INFO { public int cbStruct; public string path; public IntPtr hFile, knownSubject; }
        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_DATA
        {
            public int cbStruct; public IntPtr policy, sip; public int ui, revocation, union; public IntPtr file;
            public int stateAction; public IntPtr state, url; public int provFlags, uiContext; public IntPtr sigSettings;
        }
        [DllImport("wintrust.dll")] static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);

        /// <summary>Thumbprint of the certificate that validly signed <paramref name="file"/>, or null if unsigned / tampered.</summary>
        public static string Signer(string file)
        {
            var fi = new WINTRUST_FILE_INFO { cbStruct = Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)), path = file };
            IntPtr pf = Marshal.AllocHGlobal(fi.cbStruct);
            try
            {
                Marshal.StructureToPtr(fi, pf, false);
                var wd = new WINTRUST_DATA
                {
                    cbStruct = Marshal.SizeOf(typeof(WINTRUST_DATA)), ui = 2 /* none */, revocation = 0, union = 1 /* file */, file = pf,
                    provFlags = 0x10 | 0x1000 // no revocation check, cache only: never goes online
                };
                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
                int hr = WinVerifyTrust(IntPtr.Zero, ref action, ref wd);
                // 0 = trusted. 0x800B0109 = valid signature from a certificate this PC does not trust (self-signed):
                // still proves the file is intact and comes from the same key, which is what we compare.
                if (hr != 0 && hr != unchecked((int)0x800B0109)) return null;
                return new X509Certificate2(X509Certificate.CreateFromSignedFile(file)).Thumbprint;
            }
            catch { return null; }
            finally { Marshal.DestroyStructure(pf, typeof(WINTRUST_FILE_INFO)); Marshal.FreeHGlobal(pf); }
        }

        // ------------------------------------------------------------------ install

        /// <summary>Replaces the running exe with <paramref name="newExe"/> and restarts the app (and the wallpaper if it was running).</summary>
        public static void Install(string newExe)
        {
            string exe = Store.ExePath, old = exe + ".old";
            bool hostWasRunning = Ipc.HostRunning;
            if (hostWasRunning)
            {
                Ipc.SetEvent(Ipc.QuitEvent); // the host keeps the current wallpaper in state.json
                for (int i = 0; i < 80 && Ipc.HostRunning; i++) Thread.Sleep(50);
            }
            try
            {
                if (File.Exists(old)) { try { File.Delete(old); } catch { old = exe + "." + DateTime.Now.Ticks + ".old"; } }
                File.Move(exe, old); // allowed while running
                try { File.Copy(newExe, exe); }
                catch { File.Move(old, exe); throw; }
            }
            catch
            {
                if (hostWasRunning) Ipc.StartHost();
                throw;
            }
            if (newExe.StartsWith(UpdateDir, StringComparison.OrdinalIgnoreCase)) { try { File.Delete(newExe); } catch { } }
            if (hostWasRunning) Process.Start(new ProcessStartInfo(exe, "--host" + Store.DataArg) { UseShellExecute = false });
            UiApp.ReleaseInstance();
            Process.Start(new ProcessStartInfo(exe, (Store.DataArg + " --updated").Trim()) { UseShellExecute = false });
        }

        /// <summary>Removes the previous exe left by an update (it cannot be deleted while it runs).</summary>
        public static void Cleanup()
        {
            string dir = Store.ExeDir, name = Path.GetFileName(Store.ExePath);
            new Thread(() =>
            {
                // the previous process may still be closing: retry for a few seconds
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    bool left = false;
                    try
                    {
                        foreach (var f in Directory.GetFiles(dir, name + "*.old")) { try { File.Delete(f); } catch { left = true; } }
                        string up = UpdateDir;
                        if (Directory.Exists(up)) foreach (var f in Directory.GetFiles(up)) { try { File.Delete(f); } catch { } }
                    }
                    catch { }
                    if (!left) return;
                    Thread.Sleep(1000);
                }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        }
    }
}
