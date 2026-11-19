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
    /// <summary>
    /// Cevrimdisi haritalama icin kare kaydi: JPG + poz + ic parametreler + derinlik + ArUco + oda etiketi.
    /// Tarama baslayinca otomatik baslar. JPG kodlama ve dosya yazma arka plan is parcaciginda yapilir.
    /// </summary>
    public class KayitYoneticisi : MonoBehaviour
    {
        public ARCameraManager kamera;
        public AROcclusionManager oklusyon;
        public ARPlaneManager duzlem;
        public NoktaBulutu nokta;
        public Camera arKamera;
        public PlanHaritasi plan;
        public IsaretYoneticisi isaret;

        public const float Aralik = 0.25f;
        public const int MaksGenislik = 960, JpgKalite = 85;
        public const string ArFoundationSurum = "6.7.1";
        public const string DefaultOda = "Salon";

        public bool Kayitta { get; private set; }
        public int KareSayisi { get; private set; }
        public int AtlananKare { get; private set; }
        public float Sure => Kayitta ? Time.realtimeSinceStartup - baslangic : sonSure;
        public string SonKlasor { get; private set; } = "";
        public string Klasor => klasor;
        public string Oda { get; private set; } = DefaultOda;
        public string AdimNotu = "";
        public int Duzlem { get; private set; }
        public long YazilanBayt;
        public bool Duraklat;

        public IEnumerable<string> OdaAdlari() { return odaSirasi; }

        string klasor; float baslangic, sonSure, sonrakiZaman, sonOtoKayit;
        int sayac, bekleyenDonusum, oturum;
        DateTime baslangicZamani;
        BlockingCollection<Action> kuyruk;
        Thread yazici;
        StreamWriter jsonl;
        readonly List<Kare> bekleyen = new List<Kare>();
        readonly List<string> odaSirasi = new List<string>();
        readonly Dictionary<string, int> odaKare = new Dictionary<string, int>();
        readonly List<IsaretTespiti> tumTespitler = new List<IsaretTespiti>();
        static bool jpgIsParcacik = true;
        readonly ConcurrentQueue<Action> anaIs = new ConcurrentQueue<Action>();
        float ortIsaretMs;

        // ---- JSON kayit siniflari (alan adlari v1 ile uyumlu, yenileri eklendi) ----
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
            // v2
            public string oda;
            public bool marker_checked;
            public List<IsaretKaydi> markers = new List<IsaretKaydi>();
        }

        [Serializable]
        class IsaretKaydi
        {
            public int id; public int hamming; public float[] corners; public double timestamp_s;
        }

        class IsaretTespiti { public int frame_index; public int id; public float[] corners; public double timestamp_s; }
        [Serializable] class TespitListe { public List<TespitKaydi> detections = new List<TespitKaydi>(); }
        [Serializable] class TespitKaydi { public int frame_index; public int id; public float[] corners; public double timestamp_s; }

        [Serializable] class OdaKaydi { public string name; public int frame_count; }
        [Serializable] class IsaretOzeti { public int id; public int detections; public bool has_world_position; public V3 world_position; }
        [Serializable]
        class Ozet
        {
            public int scan_version = 2;
            public int frame_count, dropped_frames, point_count, plane_count; public float duration_s;
            public string device_model, device_name, os, arfoundation_version, unity_version, started_at, folder;
            public string notes;
            public List<OdaKaydi> rooms = new List<OdaKaydi>();
            public List<IsaretOzeti> markers_seen = new List<IsaretOzeti>();
            public string steps_note; public bool complete;
            public float avg_marker_detect_ms;
        }

        [Serializable] class V3 { public float x, y, z; }
        [Serializable]
        class DuzlemKaydi
        {
            public string id, alignment; public V3 center; public float qx, qy, qz, qw; public List<V3> boundary_world = new List<V3>();
        }
        [Serializable] class DuzlemListe { public List<DuzlemKaydi> planes = new List<DuzlemKaydi>(); }

        class Kare
        {
            public KareKaydi k; public volatile bool dosyaTamam, tespitTamam, atla; public float zaman;
        }

        // ---------------------------------------------------------------- oda
        public void OdaDegistir(string ad)
        {
            Oda = ad;
            if (!odaKare.ContainsKey(ad)) { odaKare[ad] = 0; odaSirasi.Add(ad); }
        }

        public static string Kok => Path.Combine(Application.persistentDataPath, "kayitlar");

        // ---------------------------------------------------------------- baslat / durdur
        public void Baslat()
        {
            if (Kayitta) return;
            SonKlasor = "kayit_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            klasor = Path.Combine(Kok, SonKlasor);
            Directory.CreateDirectory(klasor);
            baslangicZamani = DateTime.Now;
            oturum++; sayac = 0; KareSayisi = 0; AtlananKare = 0; bekleyenDonusum = 0; YazilanBayt = 0;
            bekleyen.Clear(); tumTespitler.Clear(); odaKare.Clear(); odaSirasi.Clear();
            OdaDegistir(Oda);
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
            baslangic = Time.realtimeSinceStartup; sonOtoKayit = baslangic;
            sonrakiZaman = 0;
            Kayitta = true;
            Debug.Log("[ArTarama] Kayit basladi: " + klasor);
        }

        void BekleyenleriBosalt(bool zorla)
        {
            // yalnizca bastan, sirayi koruyarak bosalt
            while (bekleyen.Count > 0)
            {
                var b = bekleyen[0];
                bool yas = Time.unscaledTime - b.zaman > 2f;
                if (!b.atla && !zorla && !(b.dosyaTamam && b.tespitTamam) && !yas) break;
                bekleyen.RemoveAt(0);
                if (b.atla) continue;
                var q = kuyruk;
                if (q == null || q.IsAddingCompleted) continue;
                string json = JsonUtility.ToJson(b.k);
                try { q.Add(() => { jsonl.WriteLine(json); }); }
                catch (InvalidOperationException) { }
            }
        }

        public void Durdur(bool tamam = true)
        {
            if (!Kayitta) return;
            BekleyenleriBosalt(true);
            // jsonl satirlari kuyrukta; siralama icin satirlari ters sirada bosaltmistik, sorun degil (index alani var)
            Kayitta = false;
            sonSure = Time.realtimeSinceStartup - baslangic;
            oturum++;
            var q = kuyruk;
            OzetYaz(q, tamam, true);
            q.CompleteAdding();
            Debug.Log("[ArTarama] Kayit durdu: " + klasor + " kare=" + KareSayisi);
        }

        /// <summary>Kaydi durdurur ve klasoru siler (Sifirla).</summary>
        public void Sil()
        {
            if (!Kayitta && string.IsNullOrEmpty(klasor)) return;
            Kayitta = false; oturum++;
            bekleyen.Clear();
            var q = kuyruk; var t = yazici; string dir = klasor;
            try { q?.CompleteAdding(); } catch { }
            try { if (t != null) t.Join(4000); } catch { }
            try { if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) Directory.Delete(dir, true); Debug.Log("[ArTarama] Kayit silindi: " + dir); }
            catch (Exception e) { Debug.LogWarning("[ArTarama] klasor silinemedi: " + e.Message); }
            klasor = null; KareSayisi = 0; AtlananKare = 0; kuyruk = null; yazici = null;
        }

        void OzetYaz(BlockingCollection<Action> q, bool tamam, bool son)
        {
            string dir = klasor;
            var ozet = new Ozet
            {
                frame_count = KareSayisi, dropped_frames = AtlananKare, duration_s = Sure,
                point_count = nokta != null ? nokta.Sayi : 0,
                device_model = SystemInfo.deviceModel, device_name = SystemInfo.deviceName,
                os = SystemInfo.operatingSystem, arfoundation_version = ArFoundationSurum,
                unity_version = Application.unityVersion,
                started_at = baslangicZamani.ToString("o", CultureInfo.InvariantCulture), folder = SonKlasor,
                steps_note = AdimNotu, complete = tamam, avg_marker_detect_ms = isaret != null ? isaret.OrtMs : 0,
                notes = "Goruntu sensor yonunde (manzara) kaydedildi; ic parametreler tam cozunurluk icindir, saved_w/h ile olcekle. Poz Unity oturum uzayinda (y yukari). Oda etiketi her karede 'oda' alaninda. ArUco koseleri TL,TR,BR,BL sirasiyla x,y tam kamera goruntusu pikselleri (full_w x full_h, sensor yonu)."
            };
            foreach (var o in odaSirasi) ozet.rooms.Add(new OdaKaydi { name = o, frame_count = odaKare[o] });
            if (plan != null)
                foreach (var b in plan.Isaretler.Values)
                {
                    var io = new IsaretOzeti { id = b.id, detections = b.sayi, has_world_position = b.konumVar };
                    if (b.konumVar) io.world_position = new V3 { x = b.konum.x, y = b.konum.y, z = b.konum.z };
                    ozet.markers_seen.Add(io);
                }
            var dl = DuzlemleriTopla(); ozet.plane_count = dl.planes.Count; Duzlem = dl.planes.Count;
            string duzJson = JsonUtility.ToJson(dl, true);
            string ozetJson = JsonUtility.ToJson(ozet, true);
            var tl = new TespitListe();
            foreach (var t in tumTespitler) tl.detections.Add(new TespitKaydi { frame_index = t.frame_index, id = t.id, corners = t.corners, timestamp_s = t.timestamp_s });
            string tespitJson = JsonUtility.ToJson(tl, true);
            Vector3[] pts = son && nokta != null ? nokta.Kopya() : null;

            try
            {
                q.Add(() =>
                {
                    jsonl?.Flush();
                    File.WriteAllText(Path.Combine(dir, "ozet.json"), ozetJson, new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(dir, "duzlemler.json"), duzJson, new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(dir, "isaretler.json"), tespitJson, new UTF8Encoding(false));
                    if (pts != null)
                    {
                        var ci = CultureInfo.InvariantCulture;
                        using (var w = new StreamWriter(Path.Combine(dir, "noktalar.ply"), false, new UTF8Encoding(false), 1 << 16))
                        {
                            w.Write("ply\nformat ascii 1.0\nelement vertex " + pts.Length + "\nproperty float x\nproperty float y\nproperty float z\nend_header\n");
                            var sb = new StringBuilder(1 << 16);
                            for (int i = 0; i < pts.Length; i++)
                            {
                                var p = pts[i];
                                sb.Append(p.x.ToString("F3", ci)).Append(' ').Append(p.y.ToString("F3", ci)).Append(' ').Append(p.z.ToString("F3", ci)).Append('\n');
                                if (sb.Length > 60000) { w.Write(sb.ToString()); sb.Length = 0; }
                            }
                            w.Write(sb.ToString());
                        }
                    }
                });
            }
            catch (InvalidOperationException) { }
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

        // ---------------------------------------------------------------- isaret sonucu
        public void IsaretSonucu(int kareIdx, List<ArucoSonuc> bulunan, float ms)
        {
            Kare hedef = null;
            foreach (var b in bekleyen) if (b.k.index == kareIdx) { hedef = b; break; }
            if (hedef == null) return;
            hedef.k.marker_checked = true;
            foreach (var a in bulunan)
            {
                var ik = new IsaretKaydi { id = a.id, hamming = a.hamming, corners = a.kose, timestamp_s = hedef.k.image_timestamp_s };
                hedef.k.markers.Add(ik);
                tumTespitler.Add(new IsaretTespiti { frame_index = kareIdx, id = a.id, corners = a.kose, timestamp_s = hedef.k.image_timestamp_s });
            }
            hedef.tespitTamam = true;
        }

        // ---------------------------------------------------------------- ana dongu
        void Update()
        {
            while (anaIs.TryDequeue(out var a)) { try { a(); } catch { } }
            if (!Kayitta || kamera == null) return;
            BekleyenleriBosalt(false);
            if (Duraklat) return;
            if (Time.realtimeSinceStartup - sonOtoKayit > 30f && kuyruk != null && !kuyruk.IsAddingCompleted)
            {
                sonOtoKayit = Time.realtimeSinceStartup;
                OzetYaz(kuyruk, false, false);
            }
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
            kare.oda = Oda;
            bool intrVar = kamera.TryGetIntrinsics(out XRCameraIntrinsics ki);
            if (intrVar)
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

            var kayitKare = new Kare { k = kare, zaman = Time.unscaledTime };
            bekleyen.Add(kayitKare);

            // ArUco: ayni goruntu uzerinde arka planda
            if (isaret != null && intrVar && !isaret.Mesgul && img.format == XRCpuImage.Format.AndroidYuv420_888)
            {
                try
                {
                    var pl0 = img.GetPlane(0);
                    int uz = Mathf.Min(pl0.data.Length, pl0.rowStride * img.height);
                    var buf = isaret.TamponAl(pl0.rowStride * img.height);
                    NativeArray<byte>.Copy(pl0.data, 0, buf, 0, uz);
                    isaret.Calistir(new IsaretYoneticisi.Istek
                    {
                        kareIdx = idx, w = img.width, h = img.height, satir = pl0.rowStride, zaman = img.timestamp,
                        poz = tr.position, rot = tr.rotation,
                        fx = ki.focalLength.x, fy = ki.focalLength.y, cx = ki.principalPoint.x, cy = ki.principalPoint.y,
                        rw = ki.resolution.x, rh = ki.resolution.y
                    });
                }
                catch (Exception e) { Debug.LogWarning("[ArTarama] aruco hazirlama hatasi: " + e.Message); kayitKare.tespitTamam = true; }
            }
            else kayitKare.tespitTamam = true;

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
                { AtlananKare++; kayitKare.atla = true; return; }
                byte[] ham = veri.ToArray();  // ana is parcacigi: yalnizca ham kopya
                string dir = klasor; int ww = p.outputDimensions.x, hh = p.outputDimensions.y;
                string yol = Path.Combine(dir, kare.jpg);
                try
                {
                    kuyruk.Add(() =>
                    {
                        if (jpgIsParcacik)
                        {
                            try
                            {
                                byte[] jpg = ImageConversion.EncodeArrayToJPG(ham, GraphicsFormat.R8G8B8_SRGB, (uint)ww, (uint)hh, 0, JpgKalite);
                                File.WriteAllBytes(yol, jpg); YazilanBayt += jpg.Length;
                            }
                            catch (Exception e)
                            {
                                // is parcaciginda desteklenmiyorsa ana is parcacigina don
                                jpgIsParcacik = false;
                                Debug.LogWarning("[ArTarama] JPG is parcaciginda basarisiz, ana is parcacigina donuluyor: " + e.Message);
                                anaIs.Enqueue(() => JpgAnaIs(ham, ww, hh, yol));
                            }
                        }
                        else anaIs.Enqueue(() => JpgAnaIs(ham, ww, hh, yol));
                        if (derinlik != null) { File.WriteAllBytes(Path.Combine(dir, kare.depth_file), derinlik); YazilanBayt += derinlik.Length; }
                        if (guven != null) { File.WriteAllBytes(Path.Combine(dir, kare.conf_file), guven); YazilanBayt += guven.Length; }
                        kayitKare.dosyaTamam = true;
                    });
                    KareSayisi++;
                    odaKare[kare.oda] = (odaKare.TryGetValue(kare.oda, out int onceki) ? onceki : 0) + 1;
                }
                catch (InvalidOperationException) { AtlananKare++; kayitKare.atla = true; }
            });
            img.Dispose();
        }

        void JpgAnaIs(byte[] ham, int w, int h, string yol)
        {
            try
            {
                byte[] jpg = ImageConversion.EncodeArrayToJPG(ham, GraphicsFormat.R8G8B8_SRGB, (uint)w, (uint)h, 0, JpgKalite);
                var kq = kuyruk;
                File.WriteAllBytes(yol, jpg); YazilanBayt += jpg.Length;
            }
            catch (Exception e) { Debug.LogWarning("[ArTarama] JPG hatasi: " + e.Message); }
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

        void OnDestroy() { if (Kayitta) Durdur(false); }

        void OnApplicationPause(bool p)
        {
            if (p && Kayitta)
            {
                BekleyenleriBosalt(true);
                OzetYaz(kuyruk, false, true);   // kismi ozet + nokta bulutu; kayit devam edebilir
            }
        }
    }
}
