using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArTarama
{
    /// <summary>
    /// ARCore ozellik noktalari + ortam derinliginden ham nokta kaydi (yalniz PLY ve cevrimdisi haritalama icin; EKRANDA CIZILMEZ).
    /// 2 cm voksel tekillestirme (derinlik noktalari 4 cm). Oda modeli bu sinifi kullanmaz: kanit izgarasi OdaYoneticisi'nde.
    /// </summary>
    public class NoktaBulutu : MonoBehaviour
    {
        public ARPointCloudManager yonetici;
        public AROcclusionManager oklusyon;
        public ARCameraManager kamera;
        public Camera arKamera;

        public const float Voksel = 0.02f, DerinVoksel = 0.04f;
        public const int ParcaMaks = 65000;
        public const int HaritaKatmani = 8;
        public const float MinGuven = 0.3f;

        public bool Aktif;
        public bool DerinlikNoktalari = true;
        public int Sayi { get; private set; }
        public int OzellikSayisi { get; private set; }
        public float OrtancaDerinlik { get; private set; } = -1f;

        class Parca { public Vector3[] v = new Vector3[ParcaMaks]; public int n; }

        readonly List<Parca> parcalar = new List<Parca>();
        readonly HashSet<long> gorulen = new HashSet<long>(), gorulenD = new HashSet<long>();
        float sonOzellik, sonDerinlik;
        readonly List<float> ortTmp = new List<float>(1024);
        int derinlikLog;

        public void Temizle()
        {
            parcalar.Clear(); gorulen.Clear(); gorulenD.Clear();
            Sayi = 0; OzellikSayisi = 0; OrtancaDerinlik = -1f;
        }

        /// <summary>Tum noktalarin kopyasi (PLY icin).</summary>
        public Vector3[] Kopya()
        {
            var a = new Vector3[Sayi]; int k = 0;
            foreach (var p in parcalar) { Array.Copy(p.v, 0, a, k, p.n); k += p.n; }
            return a;
        }

        void Ekle(Vector3 w)
        {
            Parca p = parcalar.Count > 0 ? parcalar[parcalar.Count - 1] : null;
            if (p == null || p.n >= ParcaMaks) { p = new Parca(); parcalar.Add(p); }
            p.v[p.n++] = w; Sayi++;
        }

        static long Anahtar(Vector3 w, float v)
        {
            long a = (long)Mathf.Floor(w.x / v) + 1048576;
            long b = (long)Mathf.Floor(w.y / v) + 1048576;
            long c = (long)Mathf.Floor(w.z / v) + 1048576;
            return (a << 42) | (b << 21) | c;
        }

        void Update()
        {
            if (!Aktif) return;
            float t = Time.unscaledTime;

            if (yonetici != null && t - sonOzellik > 0.2f)
            {
                sonOzellik = t;
                foreach (var pc in yonetici.trackables)
                {
                    var ps = pc.positions;
                    if (!ps.HasValue) continue;
                    var arr = ps.Value; var gv = pc.confidenceValues;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        if (gv.HasValue && gv.Value.IsCreated && i < gv.Value.Length && gv.Value[i] < MinGuven) continue;
                        Vector3 w = pc.transform.TransformPoint(arr[i]);
                        if (!gorulen.Add(Anahtar(w, Voksel))) continue;
                        OzellikSayisi++;
                        Ekle(w);
                    }
                }
            }

            if (oklusyon != null && kamera != null && t - sonDerinlik > 0.25f)
            {
                sonDerinlik = t;
                try { DerinlikIsle(); } catch (Exception e) { if (derinlikLog++ < 3) Debug.LogWarning("[ArTarama] derinlik noktasi hatasi: " + e.Message); }
            }
        }

        void DerinlikIsle()
        {
            if (!oklusyon.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage img)) return;
            XRCpuImage conf = default; bool confVar = false;
            if (oklusyon.TryAcquireEnvironmentDepthConfidenceCpuImage(out conf)) confVar = true;
            try
            {
                if (!kamera.TryGetIntrinsics(out XRCameraIntrinsics ki)) return;
                int dw = img.width, dh = img.height;
                var pl = img.GetPlane(0);
                bool u16 = img.format == XRCpuImage.Format.DepthUint16;
                bool f32 = img.format == XRCpuImage.Format.DepthFloat32;
                if (!u16 && !f32) return;
                var d = pl.data; int rs = pl.rowStride, ps = pl.pixelStride;
                Unity.Collections.NativeArray<byte> cf = default; int crs = 0, cps = 1;
                if (confVar) { var cp = conf.GetPlane(0); cf = cp.data; crs = cp.rowStride; cps = cp.pixelStride; }

                // orta bolgeden ortanca derinlik (yakinlik uyarisi icin)
                ortTmp.Clear();
                for (int j = dh / 4; j < dh * 3 / 4; j += 4)
                    for (int i = dw / 4; i < dw * 3 / 4; i += 4)
                    {
                        float z = Oku(d, rs, ps, i, j, u16);
                        if (z > 0.05f) ortTmp.Add(z);
                    }
                if (ortTmp.Count > 8) { ortTmp.Sort(); OrtancaDerinlik = ortTmp[ortTmp.Count / 2]; }

                if (!DerinlikNoktalari) return;
                var app = ArApp.Ornek;
                if (app == null || !app.Takipte || app.AciHizi > 35f || !ArApp.ZeminVar) return;

                var tr = arKamera.transform;
                float fx = ki.focalLength.x, fy = ki.focalLength.y, cx = ki.principalPoint.x, cy = ki.principalPoint.y;
                float sx = ki.resolution.x / (float)dw, sy = ki.resolution.y / (float)dh;
                for (int j = 0; j < dh - 1; j += 2)
                    for (int i = 0; i < dw - 1; i += 2)
                    {
                        float z = Oku(d, rs, ps, i, j, u16);
                        if (z < 0.3f || z > 4.5f) continue;
                        if (confVar && cf[j * crs + i * cps] < MinGuven * 255f) continue;
                        float z2 = Oku(d, rs, ps, i + 1, j, u16), z3 = Oku(d, rs, ps, i, j + 1, u16);
                        if (Mathf.Abs(z2 - z) > 0.12f * z || Mathf.Abs(z3 - z) > 0.12f * z) continue; // kenar / ucan piksel
                        float px = (i + 0.5f) * sx, py = (j + 0.5f) * sy;
                        Vector3 yerel = new Vector3((cy - py) / fy * z, (cx - px) / fx * z, z);
                        Vector3 w = tr.TransformPoint(yerel);
                        if (!gorulenD.Add(Anahtar(w, DerinVoksel))) continue;
                        Ekle(w);
                    }
            }
            finally { img.Dispose(); if (confVar) conf.Dispose(); }
        }

        static float Oku(Unity.Collections.NativeArray<byte> d, int rs, int ps, int i, int j, bool u16)
        {
            int off = j * rs + i * ps;
            if (u16) return (d[off] | (d[off + 1] << 8)) * 0.001f;
            byte[] b = { d[off], d[off + 1], d[off + 2], d[off + 3] };
            float f = BitConverter.ToSingle(b, 0);
            return float.IsNaN(f) ? 0f : f;
        }
    }
}
