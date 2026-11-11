using System;
using System.Runtime.InteropServices;

namespace CLWallpaper
{
    // ---------- Media Foundation "MFPlay" interop (mfplay.dll, built into Windows) ----------

    [StructLayout(LayoutKind.Sequential)]
    public struct MFVideoNormalizedRect { public float left, top, right, bottom; }

    [ComImport, Guid("766C8FFB-5FDB-4fea-A28D-B912996F51BD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFPMediaPlayerCallback
    {
        [PreserveSig] void OnMediaPlayerEvent(IntPtr pEventHeader);
    }

    [ComImport, Guid("90EB3E6B-ECBF-45cc-B1DA-C6FE3EA70D57"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFPMediaItem
    {
        void GetMediaPlayer(out IntPtr player);
        void GetURL(out IntPtr url);
        void GetObject(out IntPtr obj);
        void GetUserData(out IntPtr data);
        void SetUserData(IntPtr data);
        void GetStartStopPosition(IntPtr pguidStart, IntPtr pvStart, IntPtr pguidStop, IntPtr pvStop);
        void SetStartStopPosition(IntPtr pguidStart, IntPtr pvStart, IntPtr pguidStop, IntPtr pvStop);
        void HasVideo(out int hasVideo, out int selected);
        void HasAudio(out int hasAudio, out int selected);
        void IsProtected(out int prot);
        void GetDuration(ref Guid type, IntPtr pv);
        void GetNumberOfStreams(out int count);
        void GetStreamSelection(int idx, out int enabled);
        void SetStreamSelection(int idx, int enabled);
        void GetStreamAttribute(int idx, ref Guid attr, IntPtr pv);
        void GetPresentationAttribute(ref Guid attr, IntPtr pv);
        void GetCharacteristics(out int ch);
        void SetStreamSink(int idx, IntPtr sink);
        void GetMetadata(out IntPtr store);
    }

    [ComImport, Guid("A714590A-58AF-430a-85BF-44F5EC838D85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFPMediaPlayer
    {
        void Play();
        void Pause();
        void Stop();
        void FrameStep();
        void SetPosition(ref Guid positionType, IntPtr pv);
        void GetPosition(ref Guid positionType, IntPtr pv);
        void GetDuration(ref Guid positionType, IntPtr pv);
        void SetRate(float rate);
        void GetRate(out float rate);
        void GetSupportedRates(int forward, out float slowest, out float fastest);
        void GetState(out int state);
        void CreateMediaItemFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, int sync, IntPtr userData, out IMFPMediaItem item);
        void CreateMediaItemFromObject(IntPtr obj, int sync, IntPtr userData, out IMFPMediaItem item);
        void SetMediaItem(IMFPMediaItem item);
        void ClearMediaItem();
        void GetMediaItem(out IMFPMediaItem item);
        void GetVolume(out float v);
        void SetVolume(float v);
        void GetBalance(out float b);
        void SetBalance(float b);
        void GetMute(out int m);
        void SetMute(int m);
        void GetNativeVideoSize(out SIZE video, out SIZE ar);
        void GetIdealVideoSize(out SIZE min, out SIZE max);
        void SetVideoSourceRect(ref MFVideoNormalizedRect rc);
        void GetVideoSourceRect(out MFVideoNormalizedRect rc);
        void SetAspectRatioMode(int mode);
        void GetAspectRatioMode(out int mode);
        void GetVideoWindow(out IntPtr hwnd);
        void UpdateVideo();
        void SetBorderColor(int clr);
        void GetBorderColor(out int clr);
        void InsertEffect(IntPtr effect, int optional);
        void RemoveEffect(IntPtr effect);
        void RemoveAllEffects();
        void Shutdown();
    }

    static class MfPlay
    {
        [DllImport("mfplay.dll", PreserveSig = false)]
        public static extern void MFPCreateMediaPlayer([MarshalAs(UnmanagedType.LPWStr)] string url, int startPlayback, int options,
            IMFPMediaPlayerCallback callback, IntPtr hwnd, out IMFPMediaPlayer player);
    }

    /// <summary>Small helpers to build PROPVARIANTs in unmanaged memory.</summary>
    static class PV
    {
        [DllImport("ole32.dll")] static extern int PropVariantClear(IntPtr p);
        public static IntPtr Alloc()
        {
            IntPtr p = Marshal.AllocCoTaskMem(32);
            for (int i = 0; i < 32; i += 8) Marshal.WriteInt64(p, i, 0);
            return p;
        }
        public static IntPtr I8(long v) { IntPtr p = Alloc(); Marshal.WriteInt16(p, 0, 20); Marshal.WriteInt64(p, 8, v); return p; }
        public static IntPtr GuidNull() { return Alloc(); }
        public static long ReadI8(IntPtr p)
        {
            short vt = Marshal.ReadInt16(p, 0);
            if (vt == 20 || vt == 21) return Marshal.ReadInt64(p, 8);
            if (vt == 19 || vt == 3) return Marshal.ReadInt32(p, 8);
            return -1;
        }
        public static void Free(IntPtr p) { if (p != IntPtr.Zero) { PropVariantClear(p); Marshal.FreeCoTaskMem(p); } }
        public static void FreeRaw(IntPtr p) { if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p); }
    }

    /// <summary>
    /// A hardware-accelerated video player rendering into a window, with trimming, speed, fill modes and loops.
    /// Two engines: Media Engine + Direct3D 11 (modern codecs such as AV1) and, as a fallback, MFPlay.
    /// </summary>
    public abstract class VideoPlayer : IDisposable
    {
        public static Action<string> Trace;
        /// <summary>Set to true to always use the legacy MFPlay engine.</summary>
        public static bool ForceLegacy;

        public static VideoPlayer Create(IntPtr hwnd)
        {
            if (!ForceLegacy)
            {
                try { return new MediaEnginePlayer(hwnd); }
                catch (Exception ex) { Store.Log("Media Engine unavailable, falling back to MFPlay: " + ex.Message); }
            }
            return new MfPlayPlayer(hwnd);
        }

        public bool GlobalMute;
        public event Action CycleCompleted;
        public event Action<string> Failed;
        public event Action Loaded;
        protected void RaiseCycle() { var c = CycleCompleted; if (c != null) c(); }
        protected void RaiseFailed(string m) { var f = Failed; if (f != null) f(m); }
        protected void RaiseLoaded() { var l = Loaded; if (l != null) l(); }

        public abstract double Duration { get; }
        public abstract int NativeW { get; }
        public abstract int NativeH { get; }
        public abstract bool IsReady { get; }
        public abstract string FilePath { get; }
        public abstract double Position { get; }
        /// <summary>True when the owner should call Tick() every ~15 ms.</summary>
        public virtual bool NeedsFastTick { get { return false; } }

        public abstract void Open(string file, ItemSettings settings);
        public abstract void Close();
        public abstract void Update(ItemSettings settings);
        public abstract void SetPaused(bool paused);
        /// <summary>Call regularly (~4x/s) from the owner's UI thread: delivers events, handles loops.</summary>
        public abstract void Tick();
        public abstract void ApplyFit();
        public abstract void Repaint();
        public abstract void Seek(double sec);
        /// <summary>Preview helper: pause and show the frame at the given time.</summary>
        public abstract void Scrub(double sec);
        public abstract void EndScrub();
        public abstract void Dispose();
        /// <summary>Human-readable French message for a Media Foundation error code.</summary>
        public static string Describe(int hr)
        {
            switch ((uint)hr)
            {
                case 0xC00D5212: // MF_E_TOPO_CODEC_NOT_FOUND
                case 0xC00D36B4: // MF_E_INVALIDMEDIATYPE
                case 0xC00D6D60: // MF_E_TRANSFORM_TYPE_NOT_SET
                    return "Codec manquant pour cette vidéo. Installez l'extension correspondante depuis le Microsoft Store (Extensions vidéo HEVC, VP9 ou AV1), ou convertissez-la en MP4 H.264.";
                case 0xC00D36C4: // MF_E_UNSUPPORTED_BYTESTREAM_TYPE
                case 0xC00D36C3: // MF_E_UNSUPPORTED_SCHEME
                    return "Format de fichier non pris en charge par Windows.";
                case 0x80070002:
                case 0x80070003:
                    return "Fichier introuvable : il a peut-être été déplacé ou supprimé.";
                case 0x80070020:
                    return "Le fichier est utilisé par une autre application.";
                case 0xC00D3E85: // MF_E_BYTESTREAM_UNEXPECTED / truncated
                case 0xC00D36C5:
                    return "Fichier vidéo endommagé ou incomplet.";
                default:
                    return "Lecture impossible (code 0x" + hr.ToString("X8") + ").";
            }
        }

    }
    /// <summary>
    /// Hardware-accelerated video player that renders straight into a window (EVR),
    /// with trimming, speed, fill modes and the three loop behaviours.
    /// Framework-agnostic: used by the wallpaper host (WinForms) and the preview (WPF).
    /// </summary>
    public sealed class MfPlayPlayer : VideoPlayer, IMFPMediaPlayerCallback
    {
        const int EV_ITEM_SET = 6, EV_ERROR = 10, EV_ENDED = 11;
        static readonly Guid MF_MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        static readonly Guid MFMediaType_Audio = new Guid("73647561-0000-0010-8000-00AA00389B71");
        static bool mfStarted;

        IMFPMediaPlayer player;
        IMFPMediaItem item;
        readonly IntPtr hwnd;
        ItemSettings s = new ItemSettings();
        string path;
        bool ready, extPaused, ended, waiting, audioOn, scrubbing;
        int restartAt, version;
        double aspect;

        double duration;
        int nw, nh;
        bool fastTick;
        public override double Duration { get { return duration; } }
        public override int NativeW { get { return nw; } }
        public override int NativeH { get { return nh; } }
        public override bool IsReady { get { return ready; } }
        public override string FilePath { get { return path; } }
        public override bool NeedsFastTick { get { return fastTick; } }

        public MfPlayPlayer(IntPtr hwnd)
        {
            this.hwnd = hwnd;
            if (!mfStarted) { try { Native.MFStartup(0x20070, 0); } catch { } mfStarted = true; }
            CreatePlayer();
        }

        public override double Position
        {
            get
            {
                if (!ready || player == null) return 0;
                var g = Guid.Empty; IntPtr pv = PV.Alloc();
                try { player.GetPosition(ref g, pv); long v = PV.ReadI8(pv); return v > 0 ? v / 1e7 : 0; }
                catch { return 0; }
                finally { PV.Free(pv); }
            }
        }

        void CreatePlayer()
        {
            // MFP_OPTION_NO_MMCSS (2): a wallpaper must not get "Pro Audio/Playback" real-time priority
            // over the apps the user is actually working with.
            MfPlay.MFPCreateMediaPlayer(null, 0, 2, this, hwnd, out player);
            try { player.SetBorderColor(0); } catch { }
        }

        void DestroyPlayer()
        {
            if (player != null) { try { player.Shutdown(); } catch { } }
            ReleaseItem();
            if (player != null) { try { Marshal.ReleaseComObject(player); } catch { } player = null; }
        }

        public override void Open(string file, ItemSettings settings)
        {
            version++;
            path = file;
            s = settings.Clone();
            ready = false; ended = false; waiting = false; scrubbing = false; fastTick = false;
            if (item != null || player == null)
            {
                // Replacing the media item of a running MFPlay session is unreliable (it could stay stopped):
                // start from a fresh player instead.
                DestroyPlayer();
                CreatePlayer();
            }
            IMFPMediaItem it;
            player.CreateMediaItemFromURL(file, 1, IntPtr.Zero, out it);
            item = it;
            duration = ReadDuration();
            ApplyTrim();
            audioOn = WantAudio();
            if (!audioOn) DisableAudioStreams();
            player.SetMediaItem(item);
        }

        public override void Close()
        {
            version++;
            ready = false; ended = false; waiting = false; fastTick = false;
            DestroyPlayer(); // a fresh one is created by the next Open()
            path = null;
        }

        bool WantAudio() { return !GlobalMute && s.Volume > 0.001; }

        void DisableAudioStreams()
        {
            try
            {
                int n; item.GetNumberOfStreams(out n);
                for (int i = 0; i < n; i++)
                {
                    IntPtr pv = PV.Alloc();
                    try
                    {
                        Guid key = MF_MT_MAJOR_TYPE;
                        item.GetStreamAttribute(i, ref key, pv);
                        if (Marshal.ReadInt16(pv, 0) == 72) // VT_CLSID
                        {
                            IntPtr pg = Marshal.ReadIntPtr(pv, 8);
                            var major = (Guid)Marshal.PtrToStructure(pg, typeof(Guid));
                            if (major == MFMediaType_Audio) item.SetStreamSelection(i, 0);
                        }
                    }
                    catch { }
                    finally { PV.Free(pv); }
                }
            }
            catch { }
        }

        void ApplyTrim()
        {
            long start = (long)(Math.Max(0, s.Start) * 1e7);
            bool hasStop = s.End > 0 && (Duration <= 0 || s.End < Duration - 0.05);
            if (start <= 0 && !hasStop) return;
            IntPtr g1 = IntPtr.Zero, p1 = IntPtr.Zero, g2 = IntPtr.Zero, p2 = IntPtr.Zero;
            try
            {
                if (start > 0) { g1 = PV.GuidNull(); p1 = PV.I8(start); }
                if (hasStop) { g2 = PV.GuidNull(); p2 = PV.I8((long)(s.End * 1e7)); }
                item.SetStartStopPosition(g1, p1, g2, p2);
            }
            catch (Exception ex) { Store.Log("trim: " + ex.Message); }
            finally { PV.FreeRaw(g1); PV.Free(p1); PV.FreeRaw(g2); PV.Free(p2); }
        }

        double ReadDuration()
        {
            IntPtr pv = PV.Alloc();
            try { var g = Guid.Empty; item.GetDuration(ref g, pv); long v = PV.ReadI8(pv); return v > 0 ? v / 1e7 : 0; }
            catch { return 0; }
            finally { PV.Free(pv); }
        }



        public void OnMediaPlayerEvent(IntPtr h)
        {
            try
            {
                int type = Marshal.ReadInt32(h, 0);
                int hr = Marshal.ReadInt32(h, 4);
                var tr = Trace;
                if (tr != null) tr("event " + type + " hr=0x" + hr.ToString("X8") + " state=" + Marshal.ReadInt32(h, 8 + IntPtr.Size));
                if (type == EV_ITEM_SET)
                {
                    if (hr < 0) { Fail(hr); return; }
                    ready = true;
                    ReadNativeSize();
                    ApplyFit();
                    ApplyAudio();
                    ApplyRate();
                    player.Play();
                    if (extPaused) player.Pause();
                    RaiseLoaded();
                }
                else if (type == EV_ENDED) OnEnded();
                else if (type == EV_ERROR && hr < 0) Fail(hr);
            }
            catch (Exception ex) { Store.Log("mfplay event: " + ex.Message); }
        }

        void Fail(int hr)
        {
            Store.Log("MFPlay error 0x" + hr.ToString("X8") + " on " + path);
            RaiseFailed(Describe(hr));
        }

        void OnEnded()
        {
            if (scrubbing) return;
            ended = true;
            int v = version;
            RaiseCycle();
            if (v != version) return;
            if (s.Loop == 0) Restart();
            else if (s.Loop == 2) { waiting = true; restartAt = Environment.TickCount + (int)(Math.Max(0.5, s.PauseSec) * 1000); }
        }

        void Restart()
        {
            ended = false; waiting = false;
            Seek(s.Start);
            if (!extPaused) player.Play();
        }

        /// <summary>
        /// Call regularly (~4x/s, and every ~15 ms while NeedsFastTick) from the owner's UI timer.
        /// Handles the end of the segment *before* Media Foundation ends the session, because an ended
        /// session blanks the video for ~200 ms: looping is a seamless seek, "once"/"pause" hold the last frame.
        /// </summary>
        public override void Tick()
        {
            if (waiting && !extPaused && unchecked(Environment.TickCount - restartAt) >= 0)
            {
                try { Restart(); } catch { }
            }
            fastTick = false;
            if (!ready || extPaused || ended || waiting || scrubbing || Duration <= 0) return;
            double end = s.End > 0 && s.End < Duration ? s.End : Duration;
            double pos = Position;
            if (pos <= 0) return;
            double left = (end - pos) / Math.Max(0.25, s.Speed); // real seconds left
            fastTick = left < 0.8;
            if (left > 0.06) return;

            fastTick = false;
            int v = version;
            RaiseCycle();
            if (v != version) return;
            try
            {
                if (s.Loop == 0) Seek(s.Start); // keeps playing: no blank frame
                else
                {
                    player.Pause();
                    ended = true;
                    if (s.Loop == 2) { waiting = true; restartAt = Environment.TickCount + (int)(Math.Max(0.5, s.PauseSec) * 1000); }
                }
            }
            catch { }
        }

        public override void SetPaused(bool p)
        {
            if (extPaused == p) return;
            extPaused = p;
            if (!ready || ended || scrubbing) return;
            try { if (p) player.Pause(); else player.Play(); } catch { }
        }

        public override void Update(ItemSettings ns)
        {
            if (path == null) return;
            bool wantAudio = !GlobalMute && ns.Volume > 0.001;
            // Trim, speed and audio-track changes rebuild the session (changing them on a running
            // MFPlay session can leave it stopped); display, volume and loop mode change in place.
            bool reopen = Math.Abs(ns.Start - s.Start) > 0.001 || Math.Abs(ns.End - s.End) > 0.001
                || Math.Abs(ns.Speed - s.Speed) > 0.001 || wantAudio != audioOn;
            if (reopen || !ready) { Open(path, ns); return; }
            s = ns.Clone();
            ApplyFit(); ApplyAudio();
            if (ended && s.Loop == 0) { try { Restart(); } catch { } }
            if (waiting && s.Loop != 2) { waiting = false; if (s.Loop == 0) { try { Restart(); } catch { } } }
        }

        void ReadNativeSize()
        {
            try
            {
                SIZE v, ar;
                player.GetNativeVideoSize(out v, out ar);
                nw = v.cx; nh = v.cy;
                if (ar.cx > 0 && ar.cy > 0) aspect = (double)ar.cx / ar.cy;
                else if (v.cy > 0) aspect = (double)v.cx / v.cy;
            }
            catch { aspect = 0; }
        }

        public override void ApplyFit()
        {
            if (!ready || player == null) return;
            RECT rc; Native.GetClientRect(hwnd, out rc);
            int w = rc.Right, ht = rc.Bottom;
            if (w <= 0 || ht <= 0) return;
            var full = new MFVideoNormalizedRect { left = 0, top = 0, right = 1, bottom = 1 };
            try
            {
                if (s.Fit == 1)
                {
                    player.SetAspectRatioMode(1);
                    player.SetVideoSourceRect(ref full);
                }
                else if (s.Fit == 2 || aspect <= 0)
                {
                    player.SetAspectRatioMode(0);
                    player.SetVideoSourceRect(ref full);
                }
                else
                {
                    player.SetAspectRatioMode(0);
                    double wr = (double)w / ht;
                    var r = full;
                    if (aspect > wr) { float f = (float)(wr / aspect); r.left = (1 - f) / 2; r.right = r.left + f; }
                    else { float f = (float)(aspect / wr); r.top = (1 - f) / 2; r.bottom = r.top + f; }
                    player.SetVideoSourceRect(ref r);
                }
                player.UpdateVideo();
            }
            catch { }
        }

        void ApplyRate()
        {
            float r = (float)(s.Speed <= 0 ? 1 : s.Speed);
            try { player.SetRate(r); }
            catch { try { player.SetRate(1f); } catch { } }
        }

        void ApplyAudio()
        {
            try
            {
                bool m = GlobalMute || s.Volume <= 0.001;
                player.SetMute(m ? 1 : 0);
                if (!m) player.SetVolume((float)Math.Min(1, s.Volume));
            }
            catch { }
        }

        public override void Seek(double sec)
        {
            var g = Guid.Empty;
            IntPtr pv = PV.I8((long)(Math.Max(0, sec) * 1e7));
            try { player.SetPosition(ref g, pv); }
            finally { PV.Free(pv); }
        }

        /// <summary>Preview helper: pause and show the frame at the given time.</summary>
        public override void Scrub(double sec)
        {
            if (!ready) return;
            try
            {
                if (!scrubbing) { scrubbing = true; player.Pause(); }
                Seek(sec);
            }
            catch { }
        }

        public override void EndScrub()
        {
            if (!scrubbing) return;
            scrubbing = false;
            try { Restart(); } catch { }
        }

        public override void Repaint()
        {
            if (player == null) return;
            try { player.UpdateVideo(); } catch { }
        }

        public override void Dispose()
        {
            version++;
            ready = false;
            DestroyPlayer();
        }

        void ReleaseItem()
        {
            if (item != null) { try { Marshal.ReleaseComObject(item); } catch { } item = null; }
        }
    }
}
