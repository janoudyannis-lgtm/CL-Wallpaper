using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

namespace CLWallpaper
{
    // ======================================================================================
    //  Media Foundation "Media Engine" (the engine behind modern Windows players) + Direct3D 11.
    //  Unlike MFPlay (DirectX 9), it can use DirectX 11-only decoders such as AV1.
    // ======================================================================================

    [StructLayout(LayoutKind.Sequential)]
    struct MFARGB { public byte B, G, R, A; }

    [StructLayout(LayoutKind.Sequential)]
    struct DXGI_SWAP_CHAIN_DESC
    {
        public int Width, Height, RefreshNum, RefreshDen, Format, ScanlineOrdering, Scaling;
        public int SampleCount, SampleQuality;
        public int BufferUsage, BufferCount;
        public IntPtr OutputWindow;
        public int Windowed, SwapEffect, Flags;
    }

    [ComImport, Guid("4D645ACE-26AA-4688-9BE1-DF3516990B93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaEngineClassFactory
    {
        void CreateInstance(int flags, IMFAttributes attr, out IMFMediaEngine engine);
        void CreateTimeRange(out IntPtr range);
        void CreateError(out IntPtr error);
    }

    [ComImport, Guid("fee7c112-e776-42b5-9bbf-0048524e2bd5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaEngineNotify
    {
        [PreserveSig] int EventNotify(int ev, IntPtr param1, int param2);
    }

    [ComImport, Guid("98a1b0bb-03eb-4935-ae7c-93c1fa0e1c93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaEngine
    {
        void GetError(out IntPtr error);
        void SetErrorCode(int error);
        void SetSourceElements(IntPtr elements);
        void SetSource([MarshalAs(UnmanagedType.BStr)] string url);
        void GetCurrentSource(out IntPtr url);
        [PreserveSig] ushort GetNetworkState();
        [PreserveSig] int GetPreload();
        void SetPreload(int preload);
        void GetBuffered(out IntPtr ranges);
        void Load();
        void CanPlayType([MarshalAs(UnmanagedType.BStr)] string type, out int answer);
        [PreserveSig] ushort GetReadyState();
        [PreserveSig] int IsSeeking();
        [PreserveSig] double GetCurrentTime();
        void SetCurrentTime(double seekTime);
        [PreserveSig] double GetStartTime();
        [PreserveSig] double GetDuration();
        [PreserveSig] int IsPaused();
        [PreserveSig] double GetDefaultPlaybackRate();
        void SetDefaultPlaybackRate(double rate);
        [PreserveSig] double GetPlaybackRate();
        void SetPlaybackRate(double rate);
        void GetPlayed(out IntPtr ranges);
        void GetSeekable(out IntPtr ranges);
        [PreserveSig] int IsEnded();
        [PreserveSig] int GetAutoPlay();
        void SetAutoPlay(int autoPlay);
        [PreserveSig] int GetLoop();
        void SetLoop(int loop);
        void Play();
        void Pause();
        [PreserveSig] int GetMuted();
        void SetMuted(int muted);
        [PreserveSig] double GetVolume();
        void SetVolume(double volume);
        [PreserveSig] int HasVideo();
        [PreserveSig] int HasAudio();
        void GetNativeVideoSize(out int cx, out int cy);
        void GetVideoAspectRatio(out int cx, out int cy);
        void Shutdown();
        void TransferVideoFrame(IntPtr dstSurf, ref MFVideoNormalizedRect src, ref RECT dst, ref MFARGB border);
        [PreserveSig] int OnVideoStreamTick(out long pts);
    }

    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFAttributes
    {
        void GetItem(IntPtr a, IntPtr b);
        void GetItemType(IntPtr a, IntPtr b);
        void CompareItem(IntPtr a, IntPtr b, IntPtr c);
        void Compare(IntPtr a, int b, IntPtr c);
        void GetUINT32(ref Guid key, out int value);
        void GetUINT64(ref Guid key, out long value);
        void GetDouble(IntPtr a, IntPtr b);
        void GetGUID(ref Guid key, out Guid value);
        void GetStringLength(IntPtr a, IntPtr b);
        void GetString(IntPtr a, IntPtr b, int c, IntPtr d);
        void GetAllocatedString(IntPtr a, IntPtr b, IntPtr c);
        void GetBlobSize(IntPtr a, IntPtr b);
        void GetBlob(IntPtr a, IntPtr b, int c, IntPtr d);
        void GetAllocatedBlob(IntPtr a, IntPtr b, IntPtr c);
        void GetUnknown(IntPtr a, IntPtr b, IntPtr c);
        void SetItem(IntPtr a, IntPtr b);
        void DeleteItem(IntPtr a);
        void DeleteAllItems();
        void SetUINT32(ref Guid key, int value);
        void SetUINT64(ref Guid key, long value);
        void SetDouble(ref Guid key, double value);
        void SetGUID(ref Guid key, ref Guid value);
        void SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        void SetBlob(ref Guid key, IntPtr buf, int size);
        void SetUnknown(ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object unk);
    }

    [ComImport, Guid("eb533d5d-2db6-40f8-97a9-494692014f07"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFDXGIDeviceManager
    {
        void CloseDeviceHandle(IntPtr h);
        void GetVideoService(IntPtr h, ref Guid riid, out IntPtr svc);
        void LockDevice(IntPtr h, ref Guid riid, out IntPtr dev, int block);
        void OpenDeviceHandle(out IntPtr h);
        void ResetDevice(IntPtr device, int token);
        void TestDevice(IntPtr h);
        void UnlockDevice(IntPtr h, int save);
    }

    [ComImport, Guid("310d36a0-d2e7-4c0a-aa04-6a9d23b8886a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDXGISwapChain
    {
        void SetPrivateData(IntPtr a, int b, IntPtr c);
        void SetPrivateDataInterface(IntPtr a, IntPtr b);
        void GetPrivateData(IntPtr a, IntPtr b, IntPtr c);
        void GetParent(IntPtr a, IntPtr b);
        void GetDevice(IntPtr a, IntPtr b);
        [PreserveSig] int Present(int syncInterval, int flags);
        [PreserveSig] int GetBuffer(int buffer, ref Guid riid, out IntPtr surface);
        void SetFullscreenState(int fullscreen, IntPtr target);
        void GetFullscreenState(IntPtr a, IntPtr b);
        void GetDesc(IntPtr desc);
        [PreserveSig] int ResizeBuffers(int count, int width, int height, int format, int flags);
        void ResizeTarget(IntPtr p);
        [PreserveSig] int GetContainingOutput(out IntPtr output);
    }

    [ComImport, Guid("ae02eedb-c735-4690-8d52-5a8dc20213aa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDXGIOutput
    {
        void SetPrivateData(IntPtr a, int b, IntPtr c);
        void SetPrivateDataInterface(IntPtr a, IntPtr b);
        void GetPrivateData(IntPtr a, IntPtr b, IntPtr c);
        void GetParent(IntPtr a, IntPtr b);
        void GetDesc(IntPtr desc);
        void GetDisplayModeList(int a, int b, IntPtr c, IntPtr d);
        void FindClosestMatchingMode(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int WaitForVBlank();
    }

    [ComImport, Guid("9B7E4E00-342C-4106-A19F-4F2704F689F0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ID3D10Multithread
    {
        [PreserveSig] void Enter();
        [PreserveSig] void Leave();
        [PreserveSig] int SetMultithreadProtected(int protect);
        [PreserveSig] int GetMultithreadProtected();
    }

    static class MeNative
    {
        [DllImport("mfplat.dll", PreserveSig = false)] public static extern void MFCreateAttributes(out IMFAttributes attr, int initialSize);
        [DllImport("mfplat.dll", PreserveSig = false)] public static extern void MFCreateDXGIDeviceManager(out int resetToken, out IMFDXGIDeviceManager manager);
        [DllImport("d3d11.dll")]
        public static extern int D3D11CreateDeviceAndSwapChain(IntPtr adapter, int driverType, IntPtr software, int flags, IntPtr featureLevels, int numLevels,
            int sdkVersion, ref DXGI_SWAP_CHAIN_DESC desc, out IntPtr swapChain, out IntPtr device, out int featureLevel, out IntPtr context);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateWaitableTimerExW(IntPtr attr, string name, int flags, int access);
        [DllImport("kernel32.dll")] public static extern bool SetWaitableTimer(IntPtr h, ref long due, int period, IntPtr cb, IntPtr arg, bool resume);
        [DllImport("kernel32.dll")] public static extern int WaitForSingleObject(IntPtr h, int ms);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);

        public static readonly Guid CLSID_MFMediaEngineClassFactory = new Guid("B44392DA-499B-446b-A4CB-005FEAD0E6D5");
        public static Guid MF_MEDIA_ENGINE_CALLBACK = new Guid("c60381b8-83a4-41f8-a3d0-de05076849a9");
        public static Guid MF_MEDIA_ENGINE_DXGI_MANAGER = new Guid("065702da-1094-486d-8617-ee7cc4ee4648");
        public static Guid MF_MEDIA_ENGINE_VIDEO_OUTPUT_FORMAT = new Guid("5066893c-8cf9-42bc-8b8a-472212e52726");
        public static Guid IID_ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        public static Guid IID_ID3D10Multithread = new Guid("9B7E4E00-342C-4106-A19F-4F2704F689F0");
    }

    /// <summary>Calls COM methods by v-table slot (for the few Direct3D 11 calls that would otherwise need huge interface declarations).</summary>
    static class Vt
    {
        public static T Fn<T>(IntPtr obj, int slot) where T : class
        {
            IntPtr vt = Marshal.ReadIntPtr(obj);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, slot * IntPtr.Size), typeof(T));
        }
        public static void Release(ref IntPtr p) { if (p != IntPtr.Zero) { Marshal.Release(p); p = IntPtr.Zero; } }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct D3D11_TEXTURE2D_DESC { public int Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccess, Misc; }

    /// <summary>
    /// Cross-fade for seamless loops: keeps a copy of the last frame of the segment and blends it, fading out,
    /// over the first frames of the next loop (one full-screen triangle, GPU only, about 1 ms per frame).
    /// </summary>
    sealed class LoopFader : IDisposable
    {
        delegate int CreateTex(IntPtr self, ref D3D11_TEXTURE2D_DESC d, IntPtr init, out IntPtr tex);
        delegate int CreateView(IntPtr self, IntPtr res, IntPtr desc, out IntPtr view);
        delegate int CreateShader(IntPtr self, IntPtr code, IntPtr len, IntPtr linkage, out IntPtr shader);
        delegate int CreateBlend(IntPtr self, int[] desc, out IntPtr state);
        delegate void SetViews(IntPtr self, int start, int n, IntPtr[] views);
        delegate void SetShader(IntPtr self, IntPtr shader, IntPtr inst, int n);
        delegate void DrawFn(IntPtr self, int count, int start);
        delegate void SetPtr(IntPtr self, IntPtr p);
        delegate void SetInt(IntPtr self, int v);
        delegate void SetTargets(IntPtr self, int n, IntPtr[] rtv, IntPtr dsv);
        delegate void SetBlend(IntPtr self, IntPtr state, float[] factor, uint mask);
        delegate void SetViewports(IntPtr self, int n, float[] vp);

        [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)]
        static extern int D3DCompile(string src, IntPtr size, string name, IntPtr defines, IntPtr include, string entry, string target, int f1, int f2, out IntPtr code, out IntPtr errors);

        const string Hlsl =
            "Texture2D t : register(t0);\n" +
            "float4 vs(uint i : SV_VertexID) : SV_POSITION { float2 u = float2((i << 1) & 2, i & 2); return float4(u * float2(2, -2) + float2(-1, 1), 0, 1); }\n" +
            "float4 ps(float4 p : SV_POSITION) : SV_TARGET { return t.Load(int3(p.xy, 0)); }\n";

        readonly IntPtr dev, ctx;
        IntPtr vs, ps, blend, tex, srv;
        public int W, H;

        public LoopFader(IntPtr device, IntPtr context)
        {
            dev = device; ctx = context;
            try
            {
                vs = Shader("vs", "vs_4_0", 12);
                ps = Shader("ps", "ps_4_0", 15);
                var d = new int[2 + 8 * 8];
                for (int i = 0; i < 8; i++)
                {
                    int o = 2 + i * 8;
                    d[o] = 1; d[o + 1] = 14; d[o + 2] = 15; d[o + 3] = 1;  // src·factor + dst·(1 − factor)
                    d[o + 4] = 2; d[o + 5] = 1; d[o + 6] = 1; d[o + 7] = 0xF;
                }
                Check(Vt.Fn<CreateBlend>(dev, 20)(dev, d, out blend), "CreateBlendState");
            }
            catch { Dispose(); throw; }
        }

        static void Check(int hr, string what) { if (hr < 0) throw new COMException(what, hr); }

        IntPtr Shader(string entry, string target, int slot)
        {
            IntPtr code, err;
            int hr = D3DCompile(Hlsl, (IntPtr)Hlsl.Length, "fade", IntPtr.Zero, IntPtr.Zero, entry, target, 0, 0, out code, out err);
            Vt.Release(ref err);
            Check(hr, "D3DCompile " + entry);
            try
            {
                return CreateFromBlob(code, slot);
            }
            finally { Vt.Release(ref code); }
        }

        delegate IntPtr BlobGetPtr(IntPtr self);
        delegate IntPtr BlobGetSize(IntPtr self);
        IntPtr CreateFromBlob(IntPtr blob, int slot)
        {
            IntPtr bytes = Vt.Fn<BlobGetPtr>(blob, 3)(blob), len = Vt.Fn<BlobGetSize>(blob, 4)(blob), sh;
            Check(Vt.Fn<CreateShader>(dev, slot)(dev, bytes, len, IntPtr.Zero, out sh), "CreateShader");
            return sh;
        }

        /// <summary>The texture that receives the frozen frame, (re)created at the back-buffer size.</summary>
        public IntPtr Target(int w, int h)
        {
            if (tex != IntPtr.Zero && w == W && h == H) return tex;
            Vt.Release(ref srv); Vt.Release(ref tex);
            var d = new D3D11_TEXTURE2D_DESC { Width = w, Height = h, MipLevels = 1, ArraySize = 1, Format = 87, SampleCount = 1, BindFlags = 0x8 | 0x20 };
            Check(Vt.Fn<CreateTex>(dev, 5)(dev, ref d, IntPtr.Zero, out tex), "CreateTexture2D");
            Check(Vt.Fn<CreateView>(dev, 7)(dev, tex, IntPtr.Zero, out srv), "CreateShaderResourceView");
            W = w; H = h;
            return tex;
        }

        /// <summary>Draws the frozen frame over <paramref name="backBuffer"/> with the given opacity.</summary>
        public void Blend(IntPtr backBuffer, float alpha)
        {
            IntPtr rtv;
            Check(Vt.Fn<CreateView>(dev, 9)(dev, backBuffer, IntPtr.Zero, out rtv), "CreateRenderTargetView");
            try
            {
                Vt.Fn<SetTargets>(ctx, 33)(ctx, 1, new[] { rtv }, IntPtr.Zero);
                Vt.Fn<SetViewports>(ctx, 44)(ctx, 1, new float[] { 0, 0, W, H, 0, 1 });
                Vt.Fn<SetPtr>(ctx, 43)(ctx, IntPtr.Zero);           // default rasterizer
                Vt.Fn<SetPtr>(ctx, 17)(ctx, IntPtr.Zero);           // no input layout
                Vt.Fn<SetInt>(ctx, 24)(ctx, 4);                     // triangle list
                Vt.Fn<SetShader>(ctx, 11)(ctx, vs, IntPtr.Zero, 0);
                Vt.Fn<SetShader>(ctx, 9)(ctx, ps, IntPtr.Zero, 0);
                Vt.Fn<SetViews>(ctx, 8)(ctx, 0, 1, new[] { srv });
                Vt.Fn<SetBlend>(ctx, 35)(ctx, blend, new[] { alpha, alpha, alpha, alpha }, 0xFFFFFFFF);
                Vt.Fn<DrawFn>(ctx, 13)(ctx, 3, 0);
                // unbind, so the engine and DXGI get a clean pipeline back
                Vt.Fn<SetViews>(ctx, 8)(ctx, 0, 1, new[] { IntPtr.Zero });
                Vt.Fn<SetTargets>(ctx, 33)(ctx, 0, null, IntPtr.Zero);
                Vt.Fn<SetBlend>(ctx, 35)(ctx, IntPtr.Zero, null, 0xFFFFFFFF);
            }
            finally { Marshal.Release(rtv); }
        }

        public void Dispose()
        {
            Vt.Release(ref srv); Vt.Release(ref tex); Vt.Release(ref blend); Vt.Release(ref ps); Vt.Release(ref vs);
        }
    }

    /// <summary>Receives Media Engine events on Media Foundation worker threads.</summary>
    [ComVisible(true)]
    sealed class EngineNotify : IMFMediaEngineNotify
    {
        readonly MediaEnginePlayer owner;
        public EngineNotify(MediaEnginePlayer owner) { this.owner = owner; }
        public int EventNotify(int ev, IntPtr param1, int param2)
        {
            try { owner.OnEngineEvent(ev, param1.ToInt64(), param2); } catch { }
            return 0;
        }
    }

    /// <summary>
    /// Video player built on the Media Engine in "frame server" mode: a dedicated render thread asks the engine
    /// for each new frame, lets it scale/convert it into a DXGI back buffer (GPU) and presents it to the window,
    /// in step with the display refresh. All engine calls happen on that thread; the owner's UI thread only
    /// queues commands and receives events in Tick().
    /// </summary>
    public sealed class MediaEnginePlayer : VideoPlayer
    {
        const int EV_ERROR = 5, EV_LOADEDMETADATA = 10, EV_SEEKED = 17, EV_ENDED = 19, EV_FORMATCHANGE = 1000, EV_FIRSTFRAMEREADY = 1009;
        const int DXGI_FORMAT_B8G8R8A8_UNORM = 87;

        readonly IntPtr hwnd;
        readonly Thread thread;
        readonly ConcurrentQueue<Action> commands = new ConcurrentQueue<Action>();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly ManualResetEvent initDone = new ManualResetEvent(false);
        Exception initError;
        volatile bool stop;

        // render-thread objects
        IMFMediaEngine engine;
        IMFMediaEngineClassFactory factory;
        IMFDXGIDeviceManager manager;
        IMFAttributes attributes;
        EngineNotify notify;
        IDXGISwapChain swap;
        IDXGIOutput output;
        IntPtr device, context, timer;
        ID3D10Multithread mtLock;
        LoopFader fader;
        bool faderFailed, fading, fadeArmed;
        long fadeStart, fadeArmedAt;
        const double LoopFadeSec = 0.8;
        int resetToken, bufW, bufH;

        // shared state (written by the render thread, read by the owner)
        volatile bool ready, loadedPending, failPending, forceRedraw, metaPending, endedPending;
        volatile string failMessage;
        int cyclesPending, errCode, errHr;
        double duration, position, aspect;
        int nw, nh;

        // playback state (render thread)
        volatile ItemSettings s = new ItemSettings();
        volatile bool extPaused, scrubbing;
        bool holdEnd, waiting, hasSource;
        int restartAt;
        string path;
        int version;
        volatile int engineVersion;

        public MediaEnginePlayer(IntPtr hwnd)
        {
            this.hwnd = hwnd;
            thread = new Thread(Run) { IsBackground = true, Name = "CL-Wallpaper video" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            if (!initDone.WaitOne(8000)) { stop = true; throw new TimeoutException("Media Engine init timeout"); }
            if (initError != null) { stop = true; wake.Set(); throw initError; }
        }

        public override double Duration { get { return duration; } }
        public override int NativeW { get { return nw; } }
        public override int NativeH { get { return nh; } }
        public override bool IsReady { get { return ready; } }
        public override string FilePath { get { return path; } }
        public override double Position { get { return position; } }

        void Post(Action a) { commands.Enqueue(a); wake.Set(); }

        // ------------------------------------------------------------------ owner-thread API

        public override void Open(string file, ItemSettings settings)
        {
            path = file;
            s = settings.Clone();
            ready = false; failPending = false; loadedPending = false;
            int v = ++version;
            Post(() =>
            {
                engineVersion = v;
                holdEnd = false; waiting = false; scrubbing = false;
                duration = 0; position = 0; nw = 0; nh = 0;
                EnsureEngine();
                hasSource = true;
                engine.SetSource(file);
            });
        }

        public override void Close()
        {
            path = null;
            ready = false;
            int v = ++version;
            Post(() => { engineVersion = v; DestroyEngine(); });
        }

        public override void Update(ItemSettings ns)
        {
            if (path == null) return;
            var old = s;
            s = ns.Clone();
            Post(() =>
            {
                if (engine == null || !ready) return;
                ApplyAudioRate();
                bool trimChanged = Math.Abs(old.Start - ns.Start) > 0.001 || Math.Abs(old.End - ns.End) > 0.001;
                double end = SegmentEnd();
                if (trimChanged && (position < ns.Start - 0.05 || position > end)) engine.SetCurrentTime(ns.Start);
                if (holdEnd && ns.Loop == 0) Restart();
                if (waiting && ns.Loop != 2) { waiting = false; if (ns.Loop == 0) Restart(); }
                forceRedraw = true;
            });
        }

        public override void SetPaused(bool p)
        {
            if (extPaused == p) return;
            extPaused = p;
            Post(ApplyPlayState);
        }

        public override void Tick()
        {
            if (loadedPending) { loadedPending = false; RaiseLoaded(); }
            int n = Interlocked.Exchange(ref cyclesPending, 0);
            for (int i = 0; i < n; i++) RaiseCycle();
            if (failPending) { failPending = false; RaiseFailed(failMessage); }
        }

        public override void ApplyFit() { forceRedraw = true; wake.Set(); }
        public override void Repaint() { forceRedraw = true; wake.Set(); }

        public override void Seek(double sec) { Post(() => { if (engine != null && ready) engine.SetCurrentTime(Math.Max(0, sec)); }); }

        double scrubTarget = -1;
        public override void Scrub(double sec)
        {
            if (!ready) return;
            scrubbing = true;
            bool queued = Interlocked.Exchange(ref scrubTarget, sec) >= 0;
            if (queued) return; // a scrub command is already pending: it will use the latest target
            Post(() =>
            {
                double t = Interlocked.Exchange(ref scrubTarget, -1);
                if (engine == null || !ready || t < 0) return;
                engine.Pause();
                engine.SetCurrentTime(t);
            });
        }

        public override void EndScrub()
        {
            if (!scrubbing) return;
            scrubbing = false;
            Post(() => { if (engine != null && ready) Restart(); });
        }

        public override void Dispose()
        {
            ready = false;
            stop = true;
            wake.Set();
            thread.Join(1500);
        }

        // ------------------------------------------------------------------ events (MF worker threads)

        internal void OnEngineEvent(int ev, long p1, int p2)
        {
            var tr = Trace; if (tr != null) tr("me event " + ev + " p1=" + p1 + " p2=0x" + p2.ToString("X8"));
            switch (ev)
            {
                case EV_LOADEDMETADATA: metaPending = true; break;
                case EV_FIRSTFRAMEREADY:
                case EV_SEEKED:
                case EV_FORMATCHANGE: forceRedraw = true; break;
                case EV_ENDED: endedPending = true; break;
                case EV_ERROR: errCode = (int)p1; errHr = p2; failMessage = ErrorText((int)p1, p2); break;
                default: return;
            }
            if (ev == EV_ERROR)
            {
                Store.Log("Media Engine error " + p1 + " hr=0x" + p2.ToString("X8") + " on " + path);
                failPending = true;
            }
            wake.Set();
        }

        static string ErrorText(int code, int hr)
        {
            if (hr != 0)
            {
                string d = Describe(hr);
                if (!d.StartsWith("Lecture impossible")) return d;
            }
            switch (code)
            {
                case 3: return "La vidéo n'a pas pu être décodée : fichier endommagé ou codec défaillant.";
                case 4: return "Format ou codec non pris en charge. Installez l'extension vidéo correspondante (HEVC, VP9, AV1) depuis le Microsoft Store, ou convertissez la vidéo en MP4 H.264.";
                case 5: return "Vidéo protégée (DRM) : elle ne peut pas servir de fond d'écran.";
                default: return Describe(hr != 0 ? hr : unchecked((int)0x80004005));
            }
        }

        // ------------------------------------------------------------------ render thread

        void Run()
        {
            try
            {
                Native.MFStartup(0x20070, 0);
            try { Native.SetThreadPriority(Native.GetCurrentThread(), 1); } catch { } // above normal *within* our below-normal process: smooth frames
                timer = MeNative.CreateWaitableTimerExW(IntPtr.Zero, null, 0x2 /* HIGH_RESOLUTION */, 0x1F0003);
                if (timer == IntPtr.Zero) timer = MeNative.CreateWaitableTimerExW(IntPtr.Zero, null, 0, 0x1F0003);
                CreateDevice();
                MeNative.MFCreateDXGIDeviceManager(out resetToken, out manager);
                manager.ResetDevice(device, resetToken);
                EnsureEngine();
            }
            catch (Exception ex)
            {
                initError = ex;
                initDone.Set();
                Cleanup();
                return;
            }
            initDone.Set();

            while (!stop)
            {
                try { Step(); }
                catch (Exception ex) { Store.Log("render: " + ex.Message); SleepMs(50); }
            }
            Cleanup();
        }

        void Step()
        {
            Action a;
            while (commands.TryDequeue(out a)) { if (stop) return; a(); }

            if (engine != null && metaPending) { metaPending = false; OnMetadata(); }
            if (engine != null && endedPending) { endedPending = false; OnEnded(); }

            bool active = engine != null && ready && hasSource;
            if (active)
            {
                long pts;
                bool fresh = engine.OnVideoStreamTick(out pts) == 0;
                if (fadeArmed && ((fresh && engine.IsSeeking() == 0) || unchecked(Environment.TickCount - fadeArmedAt) > 1000))
                { fadeArmed = false; fadeStart = System.Diagnostics.Stopwatch.GetTimestamp(); }
                if (fresh || forceRedraw || fading) { forceRedraw = false; Draw(); }
                position = engine.GetCurrentTime();
                LoopLogic();
            }

            bool running = active && !extPaused && !holdEnd && !waiting && !scrubbing;
            if (running) WaitFrame();
            else wake.WaitOne(waiting ? 20 : 60);
        }

        void OnMetadata()
        {
            double d = engine.GetDuration();
            duration = double.IsNaN(d) || double.IsInfinity(d) ? 0 : d;
            int w = 0, h = 0, ax = 0, ay = 0;
            try { engine.GetNativeVideoSize(out w, out h); } catch { }
            try { engine.GetVideoAspectRatio(out ax, out ay); } catch { }
            nw = w; nh = h;
            // GetVideoAspectRatio returns the *picture* aspect ratio (e.g. 16:9), already corrected for pixel shape.
            aspect = ax > 0 && ay > 0 ? (double)ax / ay : (h > 0 ? (double)w / h : 0);
            ApplyAudioRate();
            var st = s;
            if (st.Start > 0.01 && st.Start < duration) engine.SetCurrentTime(st.Start);
            ready = true;
            forceRedraw = true;
            ApplyPlayState();
            if (engineVersion == version) loadedPending = true;
        }

        void ApplyAudioRate()
        {
            var st = s;
            bool mute = GlobalMute || st.Volume <= 0.001;
            try { engine.SetMuted(mute ? 1 : 0); if (!mute) engine.SetVolume(Math.Min(1, st.Volume)); } catch { }
            try { engine.SetPlaybackRate(st.Speed <= 0 ? 1 : st.Speed); } catch { }
        }

        void ApplyPlayState()
        {
            if (engine == null || !ready || holdEnd || waiting || scrubbing) return;
            try { if (extPaused) engine.Pause(); else engine.Play(); } catch { }
        }

        double SegmentEnd()
        {
            var st = s;
            return st.End > 0 && (duration <= 0 || st.End < duration) ? st.End : duration;
        }

        /// <summary>Handles the end of the segment just before the engine reaches it (no blank frame, no stall).</summary>
        void LoopLogic()
        {
            var st = s;
            if (scrubbing) return;
            if (waiting)
            {
                if (!extPaused && unchecked(Environment.TickCount - restartAt) >= 0) Restart();
                return;
            }
            if (holdEnd || extPaused) return;
            double end = SegmentEnd();
            if (end <= 0) return;
            double left = (end - position) / Math.Max(0.25, st.Speed);
            if (left > 0.05) return;
            Interlocked.Increment(ref cyclesPending);
            if (st.Loop == 0) { BeginCrossfade(); engine.SetCurrentTime(st.Start); }
            else
            {
                engine.Pause();
                holdEnd = true;
                if (st.Loop == 2) { waiting = true; restartAt = Environment.TickCount + (int)(Math.Max(0.5, st.PauseSec) * 1000); }
            }
        }

        void OnEnded()
        {
            // Fallback when the engine reached the real end before LoopLogic (very short segment, seek…).
            if (!ready || scrubbing || holdEnd || waiting) return;
            var st = s;
            Interlocked.Increment(ref cyclesPending);
            if (st.Loop == 0) Restart();
            else
            {
                holdEnd = true;
                if (st.Loop == 2) { waiting = true; restartAt = Environment.TickCount + (int)(Math.Max(0.5, st.PauseSec) * 1000); }
            }
        }

        void Restart()
        {
            if (holdEnd || waiting) BeginCrossfade(); // from the held last frame to the start
            holdEnd = false; waiting = false;
            engine.SetCurrentTime(s.Start);
            if (!extPaused) engine.Play();
        }

        /// <summary>Freezes the current (last) frame; Draw() then fades it out over the new loop.</summary>
        void BeginCrossfade()
        {
            fading = false; fadeArmed = false;
            var st = s;
            if (!st.LoopFade || swap == null || faderFailed || bufW <= 0 || bufH <= 0) return;
            double seg = SegmentEnd() - st.Start;
            if (seg > 0 && seg < 1.5) return; // too short to fade
            try
            {
                if (fader == null) fader = new LoopFader(device, context);
                IntPtr tex = fader.Target(bufW, bufH);
                MFVideoNormalizedRect src; int tw, th;
                Layout(out src, out tw, out th);
                var dst = new RECT { Left = 0, Top = 0, Right = bufW, Bottom = bufH };
                var border = new MFARGB { A = 255 };
                engine.TransferVideoFrame(tex, ref src, ref dst, ref border);
                fading = true; fadeArmed = true; fadeArmedAt = Environment.TickCount;
            }
            catch (Exception ex)
            {
                faderFailed = true;
                Store.Log("loop fade unavailable: " + ex.Message);
                if (fader != null) { fader.Dispose(); fader = null; }
            }
        }

        void WaitFrame()
        {
            // Pace on the display refresh; fall back to a 4 ms high-resolution timer.
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (output != null && output.WaitForVBlank() >= 0
                && System.Diagnostics.Stopwatch.GetTimestamp() - t0 > System.Diagnostics.Stopwatch.Frequency / 1000) return;
            SleepMs(4); // no vblank available (display asleep, remote session…)
        }

        void SleepMs(double ms)
        {
            if (timer == IntPtr.Zero) { Thread.Sleep((int)Math.Max(1, ms)); return; }
            long due = -(long)(ms * 10000);
            MeNative.SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false);
            MeNative.WaitForSingleObject(timer, 1000);
        }

        // ------------------------------------------------------------------ Direct3D

        void CreateDevice()
        {
            RECT rc; Native.GetClientRect(hwnd, out rc);
            bufW = Math.Max(1, rc.Right); bufH = Math.Max(1, rc.Bottom);
            var desc = new DXGI_SWAP_CHAIN_DESC
            {
                Width = bufW, Height = bufH, Format = DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleCount = 1, BufferUsage = 0x20, BufferCount = 1,
                OutputWindow = hwnd, Windowed = 1, SwapEffect = 0 // DISCARD: blit model, works in layered windows
            };
            IntPtr sc; int level;
            const int BGRA = 0x20, VIDEO = 0x800;
            int hr = MeNative.D3D11CreateDeviceAndSwapChain(IntPtr.Zero, 1 /* HARDWARE */, IntPtr.Zero, BGRA | VIDEO, IntPtr.Zero, 0, 7,
                ref desc, out sc, out device, out level, out context);
            if (hr < 0) throw new COMException("D3D11CreateDeviceAndSwapChain", hr);
            swap = (IDXGISwapChain)Marshal.GetObjectForIUnknown(sc);
            Marshal.Release(sc);

            // Media Foundation decodes on its own threads with the same device.
            IntPtr mt;
            Guid iid = MeNative.IID_ID3D10Multithread;
            if (Marshal.QueryInterface(device, ref iid, out mt) >= 0)
            {
                mtLock = (ID3D10Multithread)Marshal.GetObjectForIUnknown(mt);
                mtLock.SetMultithreadProtected(1);
                Marshal.Release(mt);
            }
            IntPtr op;
            if (swap.GetContainingOutput(out op) >= 0 && op != IntPtr.Zero)
            {
                output = (IDXGIOutput)Marshal.GetObjectForIUnknown(op);
                Marshal.Release(op);
            }
        }

        void ReleaseDevice()
        {
            fading = false; fadeArmed = false;
            if (fader != null) { fader.Dispose(); fader = null; }
            if (mtLock != null) { try { Marshal.ReleaseComObject(mtLock); } catch { } mtLock = null; }
            if (output != null) { try { Marshal.ReleaseComObject(output); } catch { } output = null; }
            if (swap != null) { try { Marshal.ReleaseComObject(swap); } catch { } swap = null; }
            if (context != IntPtr.Zero) { Marshal.Release(context); context = IntPtr.Zero; }
            if (device != IntPtr.Zero) { Marshal.Release(device); device = IntPtr.Zero; }
        }

        void RecoverDevice()
        {
            Store.Log("video: graphics device lost, recreating");
            ReleaseDevice();
            CreateDevice();
            manager.ResetDevice(device, resetToken);
            forceRedraw = true;
        }

        void EnsureEngine()
        {
            if (engine != null) return;
            if (factory == null)
                factory = (IMFMediaEngineClassFactory)Activator.CreateInstance(Type.GetTypeFromCLSID(MeNative.CLSID_MFMediaEngineClassFactory));
            if (attributes == null)
            {
                notify = new EngineNotify(this);
                MeNative.MFCreateAttributes(out attributes, 3);
                attributes.SetUnknown(ref MeNative.MF_MEDIA_ENGINE_CALLBACK, notify);
                attributes.SetUnknown(ref MeNative.MF_MEDIA_ENGINE_DXGI_MANAGER, manager);
                attributes.SetUINT32(ref MeNative.MF_MEDIA_ENGINE_VIDEO_OUTPUT_FORMAT, DXGI_FORMAT_B8G8R8A8_UNORM);
            }
            factory.CreateInstance(0, attributes, out engine);
            engine.SetAutoPlay(0);
            engine.SetLoop(0);
        }

        void DestroyEngine()
        {
            ready = false; hasSource = false; holdEnd = false; waiting = false;
            if (engine == null) return;
            try { engine.Shutdown(); } catch { }
            try { Marshal.ReleaseComObject(engine); } catch { }
            engine = null;
        }

        void Draw()
        {
            RECT rc;
            if (swap == null || !Native.GetClientRect(hwnd, out rc) || rc.Right <= 0 || rc.Bottom <= 0) return;
            MFVideoNormalizedRect src; int tw, th;
            Layout(out src, out tw, out th);
            if (tw != bufW || th != bufH)
            {
                fading = false; fadeArmed = false;
                int rhr = swap.ResizeBuffers(0, tw, th, 0, 0);
                if (rhr < 0) { HandleDeviceError(rhr); return; }
                bufW = tw; bufH = th;
            }
            IntPtr tex;
            Guid iid = MeNative.IID_ID3D11Texture2D;
            if (swap.GetBuffer(0, ref iid, out tex) < 0) return;
            try
            {
                var dst = new RECT { Left = 0, Top = 0, Right = tw, Bottom = th };
                var border = new MFARGB { A = 255 };
                engine.TransferVideoFrame(tex, ref src, ref dst, ref border);
                if (fading) BlendFade(tex);
            }
            catch (COMException) { return; } // no frame decoded yet
            finally { Marshal.Release(tex); }
            int phr = swap.Present(0, 0);
            if (phr < 0) HandleDeviceError(phr);
        }

        void BlendFade(IntPtr backBuffer)
        {
            if (fader == null || fader.W != bufW || fader.H != bufH) { fading = false; return; }
            double a = 1;
            if (!fadeArmed)
            {
                double t = (System.Diagnostics.Stopwatch.GetTimestamp() - fadeStart) / (double)System.Diagnostics.Stopwatch.Frequency;
                a = 1 - t / LoopFadeSec;
                if (a <= 0) { fading = false; return; }
                a = a * a * (3 - 2 * a); // smoothstep
            }
            if (mtLock != null) mtLock.Enter();
            try { fader.Blend(backBuffer, (float)a); }
            catch (Exception ex) { fading = false; faderFailed = true; Store.Log("loop fade: " + ex.Message); }
            finally { if (mtLock != null) mtLock.Leave(); }
        }

        /// <summary>Source crop and back-buffer size for the current fit mode.</summary>
        void Layout(out MFVideoNormalizedRect src, out int tw, out int th)
        {
            RECT rc; Native.GetClientRect(hwnd, out rc);
            int cw = Math.Max(1, rc.Right), ch = Math.Max(1, rc.Bottom);
            var st = s;
            src = new MFVideoNormalizedRect { left = 0, top = 0, right = 1, bottom = 1 };
            tw = cw; th = ch;
            if (st.Fit == 2 && nw > 0 && nh > 0)
            {
                // Stretch: render at the video's own shape, DXGI stretches the buffer to the window.
                tw = nw; th = Math.Max(1, (int)Math.Round(nw / Math.Max(0.01, aspect)));
            }
            else if (st.Fit == 0 && aspect > 0)
            {
                double wr = (double)cw / ch;
                if (aspect > wr) { float f = (float)(wr / aspect); src.left = (1 - f) / 2; src.right = src.left + f; }
                else { float f = (float)(aspect / wr); src.top = (1 - f) / 2; src.bottom = src.top + f; }
            }
        }

        void HandleDeviceError(int hr)
        {
            uint u = (uint)hr;
            if (u == 0x887A0005 || u == 0x887A0006 || u == 0x887A0007) RecoverDevice(); // removed / hung / reset
        }

        void Cleanup()
        {
            DestroyEngine();
            if (factory != null) { try { Marshal.ReleaseComObject(factory); } catch { } factory = null; }
            if (attributes != null) { try { Marshal.ReleaseComObject(attributes); } catch { } attributes = null; }
            if (manager != null) { try { Marshal.ReleaseComObject(manager); } catch { } manager = null; }
            ReleaseDevice();
            if (timer != IntPtr.Zero) { MeNative.CloseHandle(timer); timer = IntPtr.Zero; }
        }
    }
}
