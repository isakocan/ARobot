using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace ARaki.Drive
{
    /// <summary>Video kaydedicinin ortak yüzü: MP4 (telefon / Windows) ya da yedek AVI.</summary>
    public interface IVideoWriter
    {
        int Width { get; }
        int Height { get; }
        int Frames { get; }
        /// <summary>Ham RGBA kare, satırlar aşağıdan yukarı (ana iş parçacığı). Kodlama yetişmezse kare atlanır.</summary>
        void AddFrame(byte[] rgba, int w, int h);
        /// <summary>Mikrofon örnekleri (16 bit mono); hiç atılmaz.</summary>
        void AddAudio(short[] samples);
        /// <summary>Kalan işi bitirir, dosyayı kapatır, yolunu döner.</summary>
        string Close();
    }

    /// <summary>
    /// MP4 yazıcıların ortak kısmı: kareler ve ses sırayla bir iş parçacığına gider, kodlama orada yapılır (sürüş
    /// yavaşlamaz). Kare kuyruğu dolarsa kare atlanır, ses atlanmaz.
    /// </summary>
    public abstract class QueuedMp4Writer : IVideoWriter
    {
        public int Width { get; }
        public int Height { get; }
        public int Frames { get; private set; }

        protected readonly string path;
        protected readonly int fps, audioRate;
        readonly BlockingCollection<object> queue = new BlockingCollection<object>(8);
        readonly Thread worker;
        int pendingFrames;
        Exception failure;

        protected QueuedMp4Writer(string path, int width, int height, int fps, int audioRate)
        {
            this.path = path;
            Width = width; Height = height; this.fps = fps; this.audioRate = audioRate;
            worker = new Thread(Work) { IsBackground = true, Name = "MP4 kaydı" };
        }

        /// <summary>Kodlayıcıyı kurar (çağıranın iş parçacığında; hata atarsa çağıran AVI'ye döner) ve işçiyi başlatır.</summary>
        protected void Begin() => worker.Start();

        public void AddFrame(byte[] rgba, int w, int h)
        {
            if (w != Width || h != Height || Volatile.Read(ref pendingFrames) >= 4) return;
            try
            {
                if (!queue.IsAddingCompleted && queue.TryAdd(rgba)) Interlocked.Increment(ref pendingFrames);
            }
            catch (InvalidOperationException) { }
        }

        public void AddAudio(short[] samples)
        {
            if (audioRate <= 0) return;
            try { if (!queue.IsAddingCompleted) queue.Add(samples); }
            catch (InvalidOperationException) { }
        }

        void Work()
        {
            OnThreadStart();
            try
            {
                foreach (var item in queue.GetConsumingEnumerable())
                {
                    if (failure != null) continue;
                    try
                    {
                        if (item is byte[] rgba) { Interlocked.Decrement(ref pendingFrames); WriteFrame(rgba, Frames); Frames++; }
                        else if (item is short[] pcm) WriteAudio(pcm);
                    }
                    catch (Exception e) { failure = e; Debug.LogWarning("[Kayıt] MP4 yazılamadı: " + e.Message); }
                }
                try { Finish(); }
                catch (Exception e) { Debug.LogWarning("[Kayıt] MP4 kapatılamadı: " + e.Message); }
            }
            finally { OnThreadEnd(); }
        }

        public string Close()
        {
            queue.CompleteAdding();
            worker.Join();
            return path;
        }

        protected virtual void OnThreadStart() { }
        protected virtual void OnThreadEnd() { }
        protected abstract void WriteFrame(byte[] rgba, int index);
        protected abstract void WriteAudio(short[] pcm);
        protected abstract void Finish();
    }

    /// <summary>Telefonda: Android'in donanım H.264 kodlayıcısı + AAC (Plugins/Android/Mp4Kaydedici.java).</summary>
    public class AndroidMp4Writer : QueuedMp4Writer
    {
        AndroidJavaObject recorder;
        sbyte[] frame;   // Java'ya giden kopya (her karede yeniden ayrılmasın)

        public AndroidMp4Writer(string path, int width, int height, int fps, int bitrate, int audioRate)
            : base(path, width, height, fps, audioRate)
        {
            recorder = new AndroidJavaObject("com.robotsurus.kayit.Mp4Kaydedici", path, width, height, fps, bitrate, audioRate);
            Begin();
        }

        protected override void OnThreadStart() => AndroidJNI.AttachCurrentThread();
        protected override void OnThreadEnd()
        {
            recorder?.Dispose();
            recorder = null;
            AndroidJNI.DetachCurrentThread();
        }

        protected override void WriteFrame(byte[] rgba, int index)
        {
            if (frame == null || frame.Length != rgba.Length) frame = new sbyte[rgba.Length];
            Buffer.BlockCopy(rgba, 0, frame, 0, rgba.Length);
            recorder.Call("kareEkle", frame);
        }
        protected override void WriteAudio(short[] pcm) => recorder.Call("sesEkle", pcm);
        protected override void Finish() => recorder.Call("kapat");
    }

    /// <summary>
    /// Bilgisayarda (Windows): Media Foundation'ın kendi H.264 ve AAC kodlayıcısı (Windows 7+ içinde hazır; ek dosya yok).
    /// IMFSinkWriter'a NV12 kare verilir (RGBA'dan burada çevrilir), kodlamayı Windows yapar.
    /// </summary>
    public class WindowsMp4Writer : QueuedMp4Writer
    {
        readonly int bitrate;
        IMFSinkWriter writer;
        int videoStream, audioStream = -1;
        long audioSamples;
        byte[] nv12;

        public WindowsMp4Writer(string path, int width, int height, int fps, int bitrate, int audioRate)
            : base(path, width, height, fps, audioRate == 44100 || audioRate == 48000 ? audioRate : 0)   // AAC yalnızca 44,1 / 48 kHz
        {
            this.bitrate = bitrate;
            Mf.Last = width + "x" + height;
            Mf.Check(Mf.MFStartup(Mf.Version, 0), "MFStartup");
            try { Open(); }
            catch { Mf.MFShutdown(); throw; }
            Begin();
        }

        void Open()
        {
            Mf.Check(Mf.MFCreateSinkWriterFromURL(path, IntPtr.Zero, IntPtr.Zero, out writer), "dosya");
            // çıkış: H.264
            Mf.Check(Mf.MFCreateMediaType(out var outType));
            outType.SetGUID(Mf.MajorType, Mf.Video);
            outType.SetGUID(Mf.SubType, Mf.H264);
            outType.SetUINT32(Mf.AvgBitrate, (uint)bitrate);
            outType.SetUINT32(Mf.InterlaceMode, 2);   // progressive
            outType.SetUINT64(Mf.FrameSize, Pack((uint)Width, (uint)Height));
            outType.SetUINT64(Mf.FrameRate, Pack((uint)fps, 1));
            outType.SetUINT64(Mf.PixelAspect, Pack(1, 1));
            Mf.Check(writer.AddStream(outType, out videoStream), "H.264 akışı");
            // giriş: NV12 (H.264 kodlayıcının doğrudan aldığı biçim; RGB'den burada çevrilir)
            Mf.Check(Mf.MFCreateMediaType(out var inType));
            inType.SetGUID(Mf.MajorType, Mf.Video);
            inType.SetGUID(Mf.SubType, Mf.Nv12);
            inType.SetUINT32(Mf.InterlaceMode, 2);
            inType.SetUINT32(Mf.DefaultStride, (uint)Width);
            inType.SetUINT64(Mf.FrameSize, Pack((uint)Width, (uint)Height));
            inType.SetUINT64(Mf.FrameRate, Pack((uint)fps, 1));
            inType.SetUINT64(Mf.PixelAspect, Pack(1, 1));
            Mf.Check(writer.SetInputMediaType(videoStream, inType, IntPtr.Zero), "görüntü girişi");
            if (audioRate > 0)
            {
                Mf.Check(Mf.MFCreateMediaType(out var aOut));
                aOut.SetGUID(Mf.MajorType, Mf.Audio);
                aOut.SetGUID(Mf.SubType, Mf.Aac);
                aOut.SetUINT32(Mf.AudioBits, 16);
                aOut.SetUINT32(Mf.AudioRate, (uint)audioRate);
                aOut.SetUINT32(Mf.AudioChannels, 1);
                aOut.SetUINT32(Mf.AudioBytesPerSecond, 16000);   // 128 kbit/s
                Mf.Check(writer.AddStream(aOut, out audioStream), "AAC akışı");
                Mf.Check(Mf.MFCreateMediaType(out var aIn));
                aIn.SetGUID(Mf.MajorType, Mf.Audio);
                aIn.SetGUID(Mf.SubType, Mf.Pcm);
                aIn.SetUINT32(Mf.AudioBits, 16);
                aIn.SetUINT32(Mf.AudioRate, (uint)audioRate);
                aIn.SetUINT32(Mf.AudioChannels, 1);
                aIn.SetUINT32(Mf.AudioBlockAlign, 2);
                aIn.SetUINT32(Mf.AudioBytesPerSecond, (uint)audioRate * 2);
                Mf.Check(writer.SetInputMediaType(audioStream, aIn, IntPtr.Zero), "ses girişi");
            }
            Mf.Check(writer.BeginWriting(), "başlatma");
        }

        static ulong Pack(uint hi, uint lo) => ((ulong)hi << 32) | lo;

        protected override void WriteFrame(byte[] rgba, int index)
        {
            int w = Width, h = Height, n = w * h * 3 / 2;
            if (nv12 == null || nv12.Length != n) nv12 = new byte[n];
            // RGBA (satırlar aşağıdan yukarı) → NV12 (üstten: Y düzlemi, ardından iç içe U V yarım çözünürlükte), BT.601
            for (int y = 0; y < h; y++)
            {
                int src = (h - 1 - y) * w * 4, yo = y * w, uv = w * h + (y / 2) * w;
                bool chroma = (y & 1) == 0;
                for (int x = 0; x < w; x++, src += 4)
                {
                    int r = rgba[src], g = rgba[src + 1], b = rgba[src + 2];
                    nv12[yo + x] = (byte)(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);
                    if (chroma && (x & 1) == 0)
                    {
                        nv12[uv + x] = (byte)(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128);
                        nv12[uv + x + 1] = (byte)(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128);
                    }
                }
            }
            long duration = 10000000L / fps;
            Write(videoStream, nv12, n, index * duration, duration);
        }

        protected override void WriteAudio(short[] pcm)
        {
            if (audioStream < 0 || pcm.Length == 0) return;
            var bytes = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
            long start = audioSamples * 10000000L / audioRate;
            audioSamples += pcm.Length;
            Write(audioStream, bytes, bytes.Length, start, audioSamples * 10000000L / audioRate - start);
        }

        void Write(int stream, byte[] data, int length, long time, long duration)
        {
            Mf.Check(Mf.MFCreateMemoryBuffer(length, out var buffer));
            Mf.Check(buffer.Lock(out var ptr, out _, out _));
            Marshal.Copy(data, 0, ptr, length);
            Mf.Check(buffer.Unlock());
            Mf.Check(buffer.SetCurrentLength(length));
            Mf.Check(Mf.MFCreateSample(out var sample));
            Mf.Check(sample.AddBuffer(buffer));
            Mf.Check(sample.SetSampleTime(time));
            Mf.Check(sample.SetSampleDuration(duration));
            Mf.Check(writer.WriteSample(stream, sample));
            Marshal.ReleaseComObject(sample);
            Marshal.ReleaseComObject(buffer);
        }

        protected override void Finish()
        {
            try { Mf.Check(writer.Finalize_()); }
            finally
            {
                Marshal.ReleaseComObject(writer);
                writer = null;
                Mf.MFShutdown();
            }
        }
    }

    // ---------------------------------------------------------------- Media Foundation (yalnızca kullanılanlar)

    static class Mf
    {
        public const uint Version = 0x00020070;
        public static readonly Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static readonly Guid SubType = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static readonly Guid AvgBitrate = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        public static readonly Guid InterlaceMode = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        public static readonly Guid FrameSize = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        public static readonly Guid FrameRate = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        public static readonly Guid PixelAspect = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        public static readonly Guid DefaultStride = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        public static readonly Guid AudioChannels = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");
        public static readonly Guid AudioRate = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
        public static readonly Guid AudioBits = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
        public static readonly Guid AudioBytesPerSecond = new Guid("1aab75c8-cfef-451c-ab95-ac034b8e1731");
        public static readonly Guid AudioBlockAlign = new Guid("322de230-9eeb-43bd-ab7a-ff412251541d");
        public static readonly Guid Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        public static readonly Guid Audio = new Guid("73647561-0000-0010-8000-00AA00389B71");
        public static readonly Guid H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
        public static readonly Guid Nv12 = new Guid("3231564E-0000-0010-8000-00AA00389B71");
        public static readonly Guid Aac = new Guid("00001610-0000-0010-8000-00AA00389B71");
        public static readonly Guid Pcm = new Guid("00000001-0000-0010-8000-00AA00389B71");

        public static string Last = "";
        public static void Check(int hr, string step = "")
        {
            if (hr < 0) throw new InvalidOperationException($"Media Foundation {step} hata 0x{hr:X8} ({Last})");
        }

        [DllImport("mfplat.dll")] public static extern int MFStartup(uint version, uint flags);
        [DllImport("mfplat.dll")] public static extern int MFShutdown();
        [DllImport("mfplat.dll")] public static extern int MFCreateMediaType(out IMFMediaType type);
        [DllImport("mfplat.dll")] public static extern int MFCreateSample(out IMFSample sample);
        [DllImport("mfplat.dll")] public static extern int MFCreateMemoryBuffer(int max, out IMFMediaBuffer buffer);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
        public static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IntPtr attributes, out IMFSinkWriter writer);
    }

    // IMFAttributes'un 30 yöntemi sırasıyla (vtable); kullanılmayanlar yer tutucu
    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaType
    {
        void _GetItem(); void _GetItemType(); void _CompareItem(); void _Compare(); void _GetUINT32(); void _GetUINT64();
        void _GetDouble(); void _GetGUID(); void _GetStringLength(); void _GetString(); void _GetAllocatedString();
        void _GetBlobSize(); void _GetBlob(); void _GetAllocatedBlob(); void _GetUnknown(); void _SetItem(); void _DeleteItem();
        void _DeleteAllItems();
        [PreserveSig] int SetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, uint value);
        [PreserveSig] int SetUINT64([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, ulong value);
        void _SetDouble();
        [PreserveSig] int SetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [In, MarshalAs(UnmanagedType.LPStruct)] Guid value);
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSample
    {
        void _GetItem(); void _GetItemType(); void _CompareItem(); void _Compare(); void _GetUINT32(); void _GetUINT64();
        void _GetDouble(); void _GetGUID(); void _GetStringLength(); void _GetString(); void _GetAllocatedString();
        void _GetBlobSize(); void _GetBlob(); void _GetAllocatedBlob(); void _GetUnknown(); void _SetItem(); void _DeleteItem();
        void _DeleteAllItems(); void _SetUINT32(); void _SetUINT64(); void _SetDouble(); void _SetGUID(); void _SetString();
        void _SetBlob(); void _SetUnknown(); void _LockStore(); void _UnlockStore(); void _GetCount(); void _GetItemByIndex();
        void _CopyAllItems();
        void _GetSampleFlags(); void _SetSampleFlags(); void _GetSampleTime();
        [PreserveSig] int SetSampleTime(long time);
        void _GetSampleDuration();
        [PreserveSig] int SetSampleDuration(long duration);
        void _GetBufferCount(); void _GetBufferByIndex(); void _ConvertToContiguousBuffer();
        [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
    }

    [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr buffer, out int maxLength, out int currentLength);
        [PreserveSig] int Unlock();
        void _GetCurrentLength();
        [PreserveSig] int SetCurrentLength(int length);
    }

    [ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSinkWriter
    {
        [PreserveSig] int AddStream(IMFMediaType type, out int stream);
        [PreserveSig] int SetInputMediaType(int stream, IMFMediaType type, IntPtr encodingParameters);
        [PreserveSig] int BeginWriting();
        [PreserveSig] int WriteSample(int stream, IMFSample sample);
        void _SendStreamTick(); void _PlaceMarker(); void _NotifyEndOfSegment(); void _Flush();
        [PreserveSig] int Finalize_();
    }
}
