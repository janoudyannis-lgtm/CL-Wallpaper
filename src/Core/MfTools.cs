using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace CLWallpaper
{
    [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSourceReader
    {
        void GetStreamSelection(int stream, out int selected);
        void SetStreamSelection(int stream, int selected);
        [PreserveSig] int GetNativeMediaType(int stream, int typeIndex, out IMFAttributes type);
    }

    /// <summary>Video codec of a file and whether Windows can decode it.</summary>
    public sealed class CodecInfo
    {
        public string Name = "";        // "H.264", "HEVC", "AV1"…
        public bool Known;              // false when the file could not be inspected
        public bool Decodable = true;
        public string StoreId;          // Microsoft Store product of the missing extension
        public string StoreName;

        public string Problem
        {
            get
            {
                if (!Known || Decodable) return null;
                return StoreName != null
                    ? "Codec " + Name + " manquant : installez « " + StoreName + " » (gratuit) depuis le Microsoft Store."
                    : "Codec " + Name + " non pris en charge par Windows : convertissez la vidéo en MP4 H.264.";
            }
        }
    }

    /// <summary>Codec probes, remembered per file (path + modification time).</summary>
    public static class CodecCache
    {
        static readonly Dictionary<string, KeyValuePair<DateTime, CodecInfo>> map = new Dictionary<string, KeyValuePair<DateTime, CodecInfo>>(StringComparer.OrdinalIgnoreCase);
        public static CodecInfo Get(string path)
        {
            DateTime m;
            try { m = File.GetLastWriteTimeUtc(path); } catch { return new CodecInfo(); }
            KeyValuePair<DateTime, CodecInfo> hit;
            lock (map) if (map.TryGetValue(path, out hit) && hit.Key == m) return hit.Value;
            var ci = MfTools.Probe(path);
            lock (map) map[path] = new KeyValuePair<DateTime, CodecInfo>(m, ci);
            return ci;
        }
    }

    /// <summary>Media Foundation helpers: codec detection and a small H.264/MP4 encoder (GIF → MP4).</summary>
    public static class MfTools
    {
        [DllImport("mfplat.dll", PreserveSig = false)] static extern void MFStartup(int version, int flags);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void MFCreateSourceReaderFromURL(string url, IMFAttributes attr, out IMFSourceReader reader);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
        static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IMFAttributes attr, out IntPtr writer);
        [DllImport("mfplat.dll")] static extern int MFTEnumEx(Guid category, int flags, ref RegTypeInfo input, IntPtr output, out IntPtr activates, out int count);
        [DllImport("mfplat.dll")] static extern int MFCreateMediaType(out IntPtr type);
        [DllImport("mfplat.dll")] static extern int MFCreateSample(out IntPtr sample);
        [DllImport("mfplat.dll")] static extern int MFCreateMemoryBuffer(int length, out IntPtr buffer);
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] static extern void CopyMemory(IntPtr dst, IntPtr src, IntPtr len);

        [StructLayout(LayoutKind.Sequential)] struct RegTypeInfo { public Guid Major, Sub; }

        static Guid MF_MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        static Guid MF_MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        static Guid MF_MT_AVG_BITRATE = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        static Guid MF_MT_INTERLACE_MODE = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        static Guid MF_MT_FRAME_SIZE = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        static Guid MF_MT_FRAME_RATE = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        static Guid MF_MT_PIXEL_ASPECT_RATIO = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        static Guid MF_MT_DEFAULT_STRIDE = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        static Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new Guid("a634a91c-822b-41b9-a494-4de4643612b0");
        static Guid MFMediaType_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        static Guid MFVideoFormat_H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
        static Guid MFVideoFormat_RGB32 = new Guid("00000016-0000-0010-8000-00AA00389B71");
        static Guid MFVideoFormat_MPEG2 = new Guid("e06d8026-db46-11cf-b4d1-00805f6cbbea");
        static Guid MFT_CATEGORY_VIDEO_DECODER = new Guid("d6c02d4b-6833-45b4-971a-05a4b04bab91");
        static readonly Guid IID_IMFMediaType = new Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555");

        static bool started;
        static void Start() { if (!started) { MFStartup(0x20070, 0); started = true; } }

        // ------------------------------------------------------------------ codec detection

        /// <summary>Reads the video stream format and checks that a decoder is installed (≈ 20–50 ms, call off the UI thread).</summary>
        public static CodecInfo Probe(string path)
        {
            var info = new CodecInfo();
            IMFSourceReader reader = null;
            IMFAttributes type = null;
            try
            {
                Start();
                MFCreateSourceReaderFromURL(path, null, out reader);
                if (reader.GetNativeMediaType(unchecked((int)0xFFFFFFFC) /* first video stream */, 0, out type) < 0) return info;
                Guid sub;
                type.GetGUID(ref MF_MT_SUBTYPE, out sub);
                info.Known = true;
                info.Name = CodecName(sub);
                if (IsUncompressed(sub)) return info;
                var rt = new RegTypeInfo { Major = MFMediaType_Video, Sub = sub };
                IntPtr arr; int n;
                if (MFTEnumEx(MFT_CATEGORY_VIDEO_DECODER, 0x1 | 0x2 | 0x4 | 0x10 | 0x40, ref rt, IntPtr.Zero, out arr, out n) >= 0)
                {
                    for (int i = 0; i < n; i++) Marshal.Release(Marshal.ReadIntPtr(arr, i * IntPtr.Size));
                    if (arr != IntPtr.Zero) Marshal.FreeCoTaskMem(arr);
                    info.Decodable = n > 0;
                }
                if (!info.Decodable) StoreFor(info, sub);
            }
            catch (Exception ex) { Store.Log("probe " + Path.GetFileName(path) + ": " + ex.Message); }
            finally
            {
                if (type != null) Marshal.ReleaseComObject(type);
                if (reader != null) Marshal.ReleaseComObject(reader);
            }
            return info;
        }

        static string FourCC(Guid g)
        {
            byte[] b = g.ToByteArray();
            for (int i = 0; i < 4; i++) if (b[i] < 0x20 || b[i] > 0x7E) return null;
            return System.Text.Encoding.ASCII.GetString(b, 0, 4).ToUpperInvariant();
        }

        static string CodecName(Guid sub)
        {
            if (sub == MFVideoFormat_MPEG2) return "MPEG-2";
            string f = FourCC(sub);
            switch (f)
            {
                case "H264": case "AVC1": return "H.264";
                case "HEVC": case "H265": case "HVC1": return "HEVC";
                case "AV01": return "AV1";
                case "VP90": return "VP9";
                case "VP80": return "VP8";
                case "MP4V": case "M4S2": return "MPEG-4";
                case "WMV1": case "WMV2": case "WMV3": case "WVC1": return "Windows Media";
                case "MJPG": return "Motion JPEG";
                case "MP43": case "DIVX": case "XVID": case "DX50": return "DivX / Xvid";
                case "APCN": case "APCH": case "APCS": case "APCO": case "AP4H": return "Apple ProRes";
                default: return f ?? "inconnu";
            }
        }

        static bool IsUncompressed(Guid sub)
        {
            string f = FourCC(sub);
            if (sub.ToByteArray()[0] < 0x20) return true; // RGB formats (D3DFMT values)
            return f == "NV12" || f == "YUY2" || f == "UYVY" || f == "I420" || f == "IYUV" || f == "YV12" || f == "AYUV";
        }

        static void StoreFor(CodecInfo info, Guid sub)
        {
            switch (info.Name)
            {
                case "AV1": info.StoreName = "Extension vidéo AV1"; info.StoreId = "9MVZQVXJBQ9V"; break;
                case "HEVC": info.StoreName = "Extensions vidéo HEVC"; info.StoreId = "9NMZLZ57R3T7"; break;
                case "VP9": case "VP8": info.StoreName = "Extensions vidéo VP9"; info.StoreId = "9N4D0MSMP0PT"; break;
                case "MPEG-2": info.StoreName = "Extension vidéo MPEG-2"; info.StoreId = "9N95Q1ZZPMH4"; break;
            }
        }

        // ------------------------------------------------------------------ GIF → MP4

        /// <summary>True for a GIF with more than one frame.</summary>
        public static bool IsAnimatedGif(string path)
        {
            if (!string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase)) return false;
            try { using (var img = Image.FromFile(path)) return img.GetFrameCount(FrameDimension.Time) > 1; }
            catch { return false; }
        }

        /// <summary>
        /// Re-encodes an animated GIF as an H.264 MP4 (usually 5–20× smaller and decoded by the GPU instead of the CPU).
        /// Transparent areas become black. Throws on failure (the caller then keeps the GIF).
        /// </summary>
        public static void GifToMp4(string gif, string mp4)
        {
            using (var img = Image.FromFile(gif))
            {
                var dim = FrameDimension.Time;
                int frames = img.GetFrameCount(dim);
                var delays = new int[frames];
                try
                {
                    byte[] raw = img.GetPropertyItem(0x5100).Value;
                    for (int i = 0; i < frames && i * 4 + 3 < raw.Length; i++) delays[i] = BitConverter.ToInt32(raw, i * 4);
                }
                catch { }
                int total = 0, minDelay = int.MaxValue;
                for (int i = 0; i < frames; i++)
                {
                    if (delays[i] < 2) delays[i] = 10; // browsers treat 0–1 as 100 ms
                    total += delays[i];
                    minDelay = Math.Min(minDelay, delays[i]);
                }
                int fps = Math.Max(10, Math.Min(50, (int)Math.Round(100.0 / minDelay)));
                int w = (img.Width + 1) & ~1, h = (img.Height + 1) & ~1; // H.264 needs even sizes
                if (w < 32 || h < 32) throw new InvalidOperationException("GIF trop petit pour la vidéo");
                int outFrames = Math.Max(1, (int)Math.Ceiling(total / 100.0 * fps));

                string tmp = mp4 + ".part.mp4";
                try
                {
                    using (var writer = new Mp4Writer(tmp, w, h, fps, Bitrate(w, h, fps)))
                    using (var canvas = new Bitmap(w, h, PixelFormat.Format32bppRgb))
                    using (var g = Graphics.FromImage(canvas))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                        int cur = -1, acc = 0, idx = 0;
                        for (int f = 0; f < outFrames; f++)
                        {
                            double t = f * 100.0 / fps; // in 1/100 s
                            while (idx < frames - 1 && t >= acc + delays[idx]) { acc += delays[idx]; idx++; }
                            if (idx != cur)
                            {
                                img.SelectActiveFrame(dim, idx);
                                g.Clear(Color.Black);
                                g.DrawImage(img, new Rectangle(0, 0, w, h)); // odd sizes: stretched by 1 px, no black edge
                                cur = idx;
                            }
                            writer.Write(canvas);
                        }
                        writer.Finish();
                    }
                    if (File.Exists(mp4)) File.Delete(mp4);
                    File.Move(tmp, mp4);
                }
                finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
            }
        }

        public static int Bitrate(int w, int h, int fps)
        {
            return (int)Math.Max(800000, Math.Min(16000000, (long)w * h * fps / 8));
        }

        /// <summary>Minimal Sink Writer wrapper: RGB32 frames in, H.264 MP4 out.</summary>
        public sealed class Mp4Writer : IDisposable
        {
            delegate int AddStreamFn(IntPtr self, IntPtr type, out int index);
            delegate int SetInputFn(IntPtr self, int index, IntPtr type, IntPtr enc);
            delegate int NoArgFn(IntPtr self);
            delegate int WriteFn(IntPtr self, int index, IntPtr sample);
            delegate int LongFn(IntPtr self, long v);
            delegate int PtrFn(IntPtr self, IntPtr p);
            delegate int IntFn(IntPtr self, int v);
            delegate int LockFn(IntPtr self, out IntPtr data, IntPtr max, IntPtr cur);

            IntPtr writer;
            readonly int stream, w, h, fps;
            long frame;
            bool finished;

            static void Check(int hr, string what) { if (hr < 0) throw new COMException(what, hr); }

            public Mp4Writer(string path, int width, int height, int framesPerSecond, int bitrate)
            {
                Start();
                w = width; h = height; fps = framesPerSecond;
                IMFAttributes attr;
                MeNative.MFCreateAttributes(out attr, 1);
                attr.SetUINT32(ref MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
                try { Check(MFCreateSinkWriterFromURL(path, IntPtr.Zero, attr, out writer), "MFCreateSinkWriterFromURL"); }
                finally { Marshal.ReleaseComObject(attr); }

                IntPtr outType = NewType(t =>
                {
                    t.SetGUID(ref MF_MT_SUBTYPE, ref MFVideoFormat_H264);
                    t.SetUINT32(ref MF_MT_AVG_BITRATE, bitrate);
                });
                IntPtr inType = NewType(t =>
                {
                    t.SetGUID(ref MF_MT_SUBTYPE, ref MFVideoFormat_RGB32);
                    t.SetUINT32(ref MF_MT_DEFAULT_STRIDE, w * 4); // top-down rows
                });
                try
                {
                    int s;
                    Check(Vt.Fn<AddStreamFn>(writer, 3)(writer, outType, out s), "AddStream");
                    stream = s;
                    Check(Vt.Fn<SetInputFn>(writer, 4)(writer, stream, inType, IntPtr.Zero), "SetInputMediaType");
                    Check(Vt.Fn<NoArgFn>(writer, 5)(writer), "BeginWriting");
                }
                catch { Marshal.Release(outType); Marshal.Release(inType); Dispose(); throw; }
                Marshal.Release(outType); Marshal.Release(inType);
            }

            IntPtr NewType(Action<IMFAttributes> set)
            {
                IntPtr p;
                Check(MFCreateMediaType(out p), "MFCreateMediaType");
                var t = (IMFAttributes)Marshal.GetObjectForIUnknown(p);
                try
                {
                    t.SetGUID(ref MF_MT_MAJOR_TYPE, ref MFMediaType_Video);
                    t.SetUINT32(ref MF_MT_INTERLACE_MODE, 2); // progressive
                    t.SetUINT64(ref MF_MT_FRAME_SIZE, ((long)w << 32) | (uint)h);
                    t.SetUINT64(ref MF_MT_FRAME_RATE, ((long)fps << 32) | 1);
                    t.SetUINT64(ref MF_MT_PIXEL_ASPECT_RATIO, (1L << 32) | 1);
                    set(t);
                }
                finally { Marshal.ReleaseComObject(t); }
                IntPtr mt;
                Guid iid = IID_IMFMediaType;
                int hr = Marshal.QueryInterface(p, ref iid, out mt);
                Marshal.Release(p);
                Check(hr, "IMFMediaType");
                return mt;
            }

            /// <summary>Appends one frame (a 32 bpp bitmap of the writer's size).</summary>
            public void Write(Bitmap bmp)
            {
                int len = w * h * 4;
                IntPtr buf, sample = IntPtr.Zero;
                Check(MFCreateMemoryBuffer(len, out buf), "MFCreateMemoryBuffer");
                try
                {
                    IntPtr dst;
                    Check(Vt.Fn<LockFn>(buf, 3)(buf, out dst, IntPtr.Zero, IntPtr.Zero), "Lock");
                    var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                    try
                    {
                        if (bd.Stride == w * 4) CopyMemory(dst, bd.Scan0, (IntPtr)len);
                        else for (int y = 0; y < h; y++) CopyMemory(dst + y * w * 4, bd.Scan0 + y * bd.Stride, (IntPtr)(w * 4));
                    }
                    finally { bmp.UnlockBits(bd); Vt.Fn<NoArgFn>(buf, 4)(buf); }
                    Check(Vt.Fn<IntFn>(buf, 6)(buf, len), "SetCurrentLength");
                    Check(MFCreateSample(out sample), "MFCreateSample");
                    Check(Vt.Fn<PtrFn>(sample, 42)(sample, buf), "AddBuffer");
                    long dur = 10000000L / fps;
                    Check(Vt.Fn<LongFn>(sample, 36)(sample, frame * 10000000L / fps), "SetSampleTime");
                    Check(Vt.Fn<LongFn>(sample, 38)(sample, dur), "SetSampleDuration");
                    Check(Vt.Fn<WriteFn>(writer, 6)(writer, stream, sample), "WriteSample");
                    frame++;
                }
                finally
                {
                    if (sample != IntPtr.Zero) Marshal.Release(sample);
                    Marshal.Release(buf);
                }
            }

            public void Finish()
            {
                if (finished) return;
                finished = true;
                Check(Vt.Fn<NoArgFn>(writer, 11)(writer), "Finalize");
            }

            public void Dispose()
            {
                if (writer != IntPtr.Zero) { Marshal.Release(writer); writer = IntPtr.Zero; }
            }
        }
    }
}
