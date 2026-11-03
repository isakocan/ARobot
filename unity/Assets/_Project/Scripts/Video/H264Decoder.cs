using System;
using System.Runtime.InteropServices;

namespace ARaki.Video
{
    /// <summary>
    /// Cisco OpenH264 çözücüsünün ince bir sarmalayıcısı (Plugins/x86_64/openh264.dll, Plugins/Android/arm64-v8a/libopenh264.so).
    /// Bir H.264 kare (Annex B, başlangıç kodlu) verilir; kare hazırsa YUV 4:2:0 düzlemleri (Y, U, V) döner.
    /// Kütüphane C++ arayüzüdür: ISVCDecoder bir sanal fonksiyon tablosuna (vtable) işaretçidir; işlevler o tablodan
    /// sırayla alınır. Sıra ve yapı düzenleri openh264 codec_api.h / codec_app_def.h ile aynıdır.
    /// </summary>
    public sealed class H264Decoder : IDisposable
    {
        const string Lib = "openh264";

        [DllImport(Lib)] static extern int WelsCreateDecoder(out IntPtr decoder);
        [DllImport(Lib)] static extern void WelsDestroyDecoder(IntPtr decoder);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int InitializeFn(IntPtr self, IntPtr param);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int UninitializeFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DecodeNoDelayFn(IntPtr self, IntPtr src, int len, IntPtr dst, IntPtr info);

        // vtable sırası: Initialize, Uninitialize, DecodeFrame, DecodeFrameNoDelay, DecodeFrame2, FlushFrame, ...
        const int VInitialize = 0, VUninitialize = 1, VDecodeNoDelay = 3;

        // SBufferInfo (64 bit): iBufferStatus @0, zaman damgaları @8 ve @16, UsrData.sSystemBuffer @24
        // { iWidth @24, iHeight @28, iFormat @32, iStride[0] @36, iStride[1] @40 }, pDst[3] @48
        const int InfoSize = 128, OStatus = 0, OWidth = 24, OHeight = 28, OStrideY = 36, OStrideUV = 40, ODst = 48;

        IntPtr decoder;
        readonly DecodeNoDelayFn decode;
        readonly UninitializeFn uninitialize;
        IntPtr info, dst, input;
        int inputSize;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int StrideY { get; private set; }
        public int StrideUV { get; private set; }

        public H264Decoder()
        {
            if (WelsCreateDecoder(out decoder) != 0 || decoder == IntPtr.Zero) throw new Exception("OpenH264 çözücü oluşturulamadı");
            IntPtr vtable = Marshal.ReadIntPtr(decoder);
            T Fn<T>(int i) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, i * IntPtr.Size));
            var init = Fn<InitializeFn>(VInitialize);
            uninitialize = Fn<UninitializeFn>(VUninitialize);
            decode = Fn<DecodeNoDelayFn>(VDecodeNoDelay);

            // SDecodingParam (64 bit): pFileNameRestructed @0, uiCpuLoad @8, uiTargetDqLayer @12, eEcActiveIdc @16,
            // bParseOnly @20, sVideoProperty { size @24, eVideoBsType @28 }
            IntPtr param = Marshal.AllocHGlobal(64);
            for (int i = 0; i < 64; i++) Marshal.WriteByte(param, i, 0);
            Marshal.WriteByte(param, 12, 0xFF);      // tüm katmanlar
            Marshal.WriteInt32(param, 16, 0);        // hata gizleme kapalı: bozuk kare yerine bir sonrakini bekle
            Marshal.WriteInt32(param, 24, 8);        // sizeof(SVideoProperty)
            Marshal.WriteInt32(param, 28, 0);        // VIDEO_BITSTREAM_AVC
            int r = init(decoder, param);
            Marshal.FreeHGlobal(param);
            if (r != 0) throw new Exception("OpenH264 başlatılamadı: " + r);

            info = Marshal.AllocHGlobal(InfoSize);
            dst = Marshal.AllocHGlobal(3 * IntPtr.Size);
        }

        /// <summary>
        /// Bir H.264 erişim birimini çözer. Kare hazırsa true; Y, U, V düzlemleri verilen dizilere (gerekirse
        /// büyütülerek) kopyalanır. Düzlem boyutları: Y = StrideY x Height, U ve V = StrideUV x Height/2.
        /// </summary>
        public bool Decode(byte[] data, int offset, int length, ref byte[] y, ref byte[] u, ref byte[] v)
        {
            if (length > inputSize)
            {
                if (input != IntPtr.Zero) Marshal.FreeHGlobal(input);
                inputSize = Math.Max(length, 256 * 1024);
                input = Marshal.AllocHGlobal(inputSize);
            }
            Marshal.Copy(data, offset, input, length);
            for (int i = 0; i < InfoSize; i += 4) Marshal.WriteInt32(info, i, 0);
            decode(decoder, input, length, dst, info);
            if (Marshal.ReadInt32(info, OStatus) != 1) return false;

            Width = Marshal.ReadInt32(info, OWidth);
            Height = Marshal.ReadInt32(info, OHeight);
            StrideY = Marshal.ReadInt32(info, OStrideY);
            StrideUV = Marshal.ReadInt32(info, OStrideUV);
            int ySize = StrideY * Height, uvSize = StrideUV * (Height / 2);
            if (y == null || y.Length != ySize) y = new byte[ySize];
            if (u == null || u.Length != uvSize) u = new byte[uvSize];
            if (v == null || v.Length != uvSize) v = new byte[uvSize];
            Marshal.Copy(Marshal.ReadIntPtr(info, ODst), y, 0, ySize);
            Marshal.Copy(Marshal.ReadIntPtr(info, ODst + IntPtr.Size), u, 0, uvSize);
            Marshal.Copy(Marshal.ReadIntPtr(info, ODst + 2 * IntPtr.Size), v, 0, uvSize);
            return true;
        }

        public void Dispose()
        {
            if (decoder != IntPtr.Zero)
            {
                uninitialize(decoder);
                WelsDestroyDecoder(decoder);
                decoder = IntPtr.Zero;
            }
            if (info != IntPtr.Zero) { Marshal.FreeHGlobal(info); info = IntPtr.Zero; }
            if (dst != IntPtr.Zero) { Marshal.FreeHGlobal(dst); dst = IntPtr.Zero; }
            if (input != IntPtr.Zero) { Marshal.FreeHGlobal(input); input = IntPtr.Zero; }
        }
    }
}
