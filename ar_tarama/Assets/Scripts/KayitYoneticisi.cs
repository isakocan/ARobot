using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArTarama
{
    /// <summary>Cevrimdisi haritalama icin kare kaydi: JPG + poz + ic parametreler + derinlik.</summary>
    public class KayitYoneticisi : MonoBehaviour
    {
        public ARCameraManager kamera;
        public AROcclusionManager oklusyon;
        public ARPlaneManager duzlem;
        public NoktaBulutu nokta;
        public Camera arKamera;

        public const float Aralik = 0.25f;
        public const int MaksGenislik = 960, JpgKalite = 85;
        public const string ArFoundationSurum = "6.7.1";

        public bool Kayitta { get; private set; }
        public int KareSayisi { get; private set; }
        public int AtlananKare { get; private set; }
        public float Sure => Kayitta ? Time.realtimeSinceStartup - baslangic : sonSure;
        public string SonKlasor { get; private set; } = "";

        string klasor; float baslangic, sonSure, sonrakiZaman;
        int sayac, bekleyenDonusum, oturum;
        DateTime baslangicZamani;
        BlockingCollection<Action> kuyruk;
        Thread yazici;
        StreamWriter jsonl;

        // ---- JSON kayit sinifları ----
        [Serializable]
        class KareKaydi
        {
            public int index; public long timestamp_ns; public double image_timestamp_s; public float unity_time;
            public float px, py, pz, qx, qy, qz, qw;
            public string pose_note = "Unity oturum uzayi (sol el), AR kamera transform, goruntu aninda";
            public bool has_intrinsics; public float fx, fy, cx, cy; public int intrinsics_w, intrinsics_h;
            public int full_w, full_h, saved_w, saved_h; public string jpg;
            public string depth_file, depth_format, depth_unit; public int depth_w, depth_h, depth_pixel_stride; public double depth_timestamp_s;
            public string conf_file, conf_format; public int conf_w, conf_h, conf_pixel_stride;
            public string display_orientation; public int screen_w, screen_h;
            public bool has_floor; public float floor_y;
        }

        [Serializable] class Ozet
        {
            public int frame_count, dropped_frames, point_count, plane_count; public float duration_s;
            public string device_model, device_name, os, arfoundation_version, unity_version, started_at, folder;
            public string notes;
        }

        [Serializable] class V3 { public float x, y, z; }
        [Serializable] class DuzlemKaydi
        {
            public string id, alignment; public V3 center; public float qx, qy, qz, qw; public List<V3> boundary_world = new List<V3>();
        }
        [Serializable] class DuzlemListe { public List<DuzlemKaydi> planes = new List<DuzlemKaydi>(); }

        public void Baslat()
        {
            if (Kayitta) return;
            string kok = Path.Combine(Application.persistentDataPath, "kayitlar");
            SonKlasor = "kayit_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            klasor = Path.Combine(kok, SonKlasor);
            Directory.CreateDirectory(klasor);
            baslangicZamani = DateTime.Now;
            oturum++; sayac = 0; KareSayisi = 0; AtlananKare = 0; bekleyenDonusum = 0;
            kuyruk = new BlockingCollection<Action>(new ConcurrentQueue<Action>());
            var q = kuyruk;
            string jl = Path.Combine(klasor, "kareler.jsonl");
            yazici = new Thread(() =>
            {
                try
                {
                    jsonl = new StreamWriter(new FileStream(jl, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
                    foreach (var a in q.GetConsumingEnumerable())
                    {
                        try { a(); } catch (Exception e) { Debug.LogWarning("[ArTarama] yazma hatasi: " + e.Message); }
                    }
                }
                finally { try { jsonl?.Flush(); jsonl?.Dispose(); } catch { } }
            }) { IsBackground = true, Name = "KayitYazici" };
            yazici.Start();
            baslangic = Time.realtimeSinceStartup;
            sonrakiZaman = 0;
            Kayitta = true;
            Debug.Log("[ArTarama] Kayit basladi: " + klasor);
        }

        public void Durdur()
        {
            if (!Kayitta) return;
            Kayitta = false;
            sonSure = Time.realtimeSinceStartup - baslangic;
            oturum++; // bekleyen donusumleri gecersiz kil
            var q = kuyruk; string dir = klasor;

            var ozet = new Ozet
            {
                frame_count = KareSayisi, dropped_frames = AtlananKare, duration_s = sonSure,
                point_count = nokta != null ? nokta.Sayi : 0,
                device_model = SystemInfo.deviceModel, device_name = SystemInfo.deviceName,
                os = SystemInfo.operatingSystem, arfoundation_version = ArFoundationSurum,
                unity_version = Application.unityVersion,
                started_at = baslangicZamani.ToString("o", CultureInfo.InvariantCulture), folder = SonKlasor,
                notes = "Goruntu sensor yonunde (manzara) kaydedildi; ic parametreler tam cozunurluk icindir, saved_w/h ile olcekle. Poz Unity oturum uzayinda (y yukari)."
            };
            var pts = nokta != null ? nokta.Kopya() : new Vector3[0];
            var dl = DuzlemleriTopla();
            ozet.plane_count = dl.planes.Count;
            string duzJson = JsonUtility.ToJson(dl, true);
            string ozetJson = JsonUtility.ToJson(ozet, true);

            q.Add(() =>
            {
                jsonl?.Flush();
                File.WriteAllText(Path.Combine(dir, "ozet.json"), ozetJson, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(dir, "duzlemler.json"), duzJson, new UTF8Encoding(false));
                var sb = new StringBuilder(pts.Length * 28 + 256);
                sb.Append("ply\nformat ascii 1.0\nelement vertex ").Append(pts.Length).Append("\nproperty float x\nproperty float y\nproperty float z\nend_header\n");
                var ci = CultureInfo.InvariantCulture;
                foreach (var p in pts) sb.Append(p.x.ToString("R", ci)).Append(' ').Append(p.y.ToString("R", ci)).Append(' ').Append(p.z.ToString("R", ci)).Append('\n');
                File.WriteAllText(Path.Combine(dir, "noktalar.ply"), sb.ToString(), new UTF8Encoding(false));
            });
            q.CompleteAdding();
            Debug.Log("[ArTarama] Kayit durdu: " + dir + " kare=" + KareSayisi);
        }

        DuzlemListe DuzlemleriTopla()
        {
            var l = new DuzlemListe();
            if (duzlem == null) return l;
            foreach (var p in duzlem.trackables)
            {
                var k = new DuzlemKaydi
                {
                    id = p.trackableId.ToString(), alignment = p.alignment.ToString(),
                    center = new V3 { x = p.transform.position.x, y = p.transform.position.y, z = p.transform.position.z },
                    qx = p.transform.rotation.x, qy = p.transform.rotation.y, qz = p.transform.rotation.z, qw = p.transform.rotation.w
                };
                foreach (var b in p.boundary)
                {
                    Vector3 w = p.transform.TransformPoint(new Vector3(b.x, 0, b.y));
                    k.boundary_world.Add(new V3 { x = w.x, y = w.y, z = w.z });
                }
                l.planes.Add(k);
            }
            return l;
        }

        void Update()
        {
            if (!Kayitta || kamera == null) return;
            if (Time.unscaledTime < sonrakiZaman) return;
            if (!ArApp.Ornek.Takipte) return;
            if (bekleyenDonusum >= 2 || (kuyruk != null && kuyruk.Count >= 8)) { AtlananKare++; sonrakiZaman = Time.unscaledTime + Aralik; return; }
            sonrakiZaman = Time.unscaledTime + Aralik;

            if (!kamera.TryAcquireLatestCpuImage(out XRCpuImage img)) return;

            var kare = new KareKaydi();
            int idx = sayac++;
            kare.index = idx;
            kare.timestamp_ns = ArApp.Ornek.SonZamanNs;
            kare.image_timestamp_s = img.timestamp;
            kare.unity_time = Time.time;
            var tr = arKamera.transform;
            kare.px = tr.position.x; kare.py = tr.position.y; kare.pz = tr.position.z;
            kare.qx = tr.rotation.x; kare.qy = tr.rotation.y; kare.qz = tr.rotation.z; kare.qw = tr.rotation.w;
            kare.full_w = img.width; kare.full_h = img.height;
            if (kamera.TryGetIntrinsics(out XRCameraIntrinsics ki))
            {
                kare.has_intrinsics = true;
                kare.fx = ki.focalLength.x; kare.fy = ki.focalLength.y;
                kare.cx = ki.principalPoint.x; kare.cy = ki.principalPoint.y;
                kare.intrinsics_w = ki.resolution.x; kare.intrinsics_h = ki.resolution.y;
            }
            kare.display_orientation = Screen.orientation.ToString();
            kare.screen_w = Screen.width; kare.screen_h = Screen.height;
            kare.has_floor = ArApp.ZeminVar; kare.floor_y = ArApp.ZeminY;
            string ad = idx.ToString("D6");
            kare.jpg = "kare_" + ad + ".jpg";

            byte[] derinlik = null, guven = null;
            if (oklusyon != null)
            {
                if (oklusyon.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage d))
                {
                    derinlik = KompaktKopya(d);
                    kare.depth_file = "derinlik_" + ad + ".bin";
                    kare.depth_format = d.format.ToString();
                    kare.depth_unit = d.format == XRCpuImage.Format.DepthFloat32 ? "metre (float32 little-endian)" :
                                      d.format == XRCpuImage.Format.DepthUint16 ? "milimetre (uint16 little-endian)" : "bilinmiyor";
                    kare.depth_w = d.width; kare.depth_h = d.height;
                    kare.depth_pixel_stride = d.GetPlane(0).pixelStride;
                    kare.depth_timestamp_s = d.timestamp;
                    d.Dispose();
                }
                if (oklusyon.TryAcquireEnvironmentDepthConfidenceCpuImage(out XRCpuImage c))
                {
                    guven = KompaktKopya(c);
                    kare.conf_file = "guven_" + ad + ".bin";
                    kare.conf_format = c.format.ToString();
                    kare.conf_w = c.width; kare.conf_h = c.height;
                    kare.conf_pixel_stride = c.GetPlane(0).pixelStride;
                    c.Dispose();
                }
            }

            int w = Mathf.Min(img.width, MaksGenislik);
            int h = Mathf.RoundToInt(img.height * (w / (float)img.width));
            kare.saved_w = w; kare.saved_h = h;
            var prm = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, img.width, img.height),
                outputDimensions = new Vector2Int(w, h),
                outputFormat = TextureFormat.RGB24,
                transformation = XRCpuImage.Transformation.None
            };
            int benimOturum = oturum;
            bekleyenDonusum++;
            img.ConvertAsync(prm, (durum, p, veri) =>
            {
                bekleyenDonusum--;
                if (durum != XRCpuImage.AsyncConversionStatus.Ready || benimOturum != oturum || kuyruk == null || kuyruk.IsAddingCompleted)
                { AtlananKare++; return; }
                byte[] jpg;
                try { jpg = ImageConversion.EncodeArrayToJPG(veri.ToArray(), GraphicsFormat.R8G8B8_SRGB, (uint)p.outputDimensions.x, (uint)p.outputDimensions.y, 0, JpgKalite); }
                catch (Exception e) { Debug.LogWarning("[ArTarama] JPG hatasi: " + e.Message); AtlananKare++; return; }
                string json = JsonUtility.ToJson(kare);
                string dir = klasor;
                try
                {
                    kuyruk.Add(() =>
                    {
                        File.WriteAllBytes(Path.Combine(dir, kare.jpg), jpg);
                        if (derinlik != null) File.WriteAllBytes(Path.Combine(dir, kare.depth_file), derinlik);
                        if (guven != null) File.WriteAllBytes(Path.Combine(dir, kare.conf_file), guven);
                        jsonl.WriteLine(json);
                    });
                    KareSayisi++;
                }
                catch (InvalidOperationException) { AtlananKare++; }
            });
            img.Dispose();
        }

        /// <summary>Satir doldurmasini (rowStride) atip sikisik bayt dizisi uretir.</summary>
        static byte[] KompaktKopya(XRCpuImage im)
        {
            var pl = im.GetPlane(0);
            int satirBayt = im.width * pl.pixelStride;
            var cikis = new byte[satirBayt * im.height];
            var d = pl.data;
            for (int y = 0; y < im.height; y++)
                NativeArray<byte>.Copy(d, y * pl.rowStride, cikis, y * satirBayt, satirBayt);
            return cikis;
        }

        void OnDestroy() { if (Kayitta) Durdur(); }
        void OnApplicationPause(bool p) { if (p && Kayitta) Durdur(); }
    }
}
