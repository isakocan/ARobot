using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ARaki.Robot;
using ARaki.Theming;
using ARaki.Video;
using ARaki.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ARaki.EditorTools
{
    /// <summary>
    /// Konum/harita yöntemlerini çevrimdışı denemek için sentetik veri kümesi üretir (KAYIT BİÇİMİ v1, docs/ar-konum-yol-haritasi.md Aşama 1).
    /// Ev sahnesinde (DriveSim) komutlu yollar boyunca 15 kare/sn robot-göreli eşdikdörtgen panorama, gerçek konum,
    /// gerçek ızgara harita ve her yön için gerçek mesafe yazar. Sahne editörde (pencere açmadan) açılır; oyun modu gerekmez.
    ///
    /// Komut satırı (grafik açık, -nographics KULLANMA):
    ///   Unity -batchmode -quit -projectPath unity -executeMethod ARaki.EditorTools.KonumVeriUretici.Uret
    /// Ortam değişkenleri (hepsi isteğe bağlı):
    ///   ARAKI_KAYIT_KOK   çıktı kökü (varsayılan: depo/kayitlar/sim)
    ///   ARAKI_YOLLAR      virgülle ayrılmış kayıt adları ya da "hepsi" (varsayılan: hepsi)
    ///   ARAKI_HIZ         ileri hız m/sn (0.40)          ARAKI_DONUS_HIZI  dönüş hızı derece/sn (60)
    ///   ARAKI_SAPMA       düz giderken sapma derece/m (2.0, sağa)
    ///   ARAKI_KURU        1: panorama ve ışın yazma, yalnız yol + çarpışma doğrulaması + yol.png
    ///   ARAKI_KARE_SINIRI her kayıtta en çok bu kadar kare (deneme için)
    ///   ARAKI_ASCII       dosya yolu: engel haritasını metin olarak yazar (yol tasarlamak için)
    ///   ARAKI_SADECE_GERCEK 1: panoramaya dokunmaz; yolları aynı tohumla yeniden üretir (gercek_konum.csv dosyadakiyle
    ///                     karşılaştırılır, YAZILMAZ), yalnız gerçek harita / mesafe dosyalarını ve bilgi.json "gercek_tanimlari"
    ///                     anahtarını yeniler; dosyaları kayitlar/theta/&lt;ad&gt;/ altına da kopyalar.
    /// Diğer girişler: KonumVeriUretici.Kontrol (yön/ileri doğrulaması), KonumVeriUretici.HaritaYaz.
    /// Yalnız gerçek veriyi yenileme komutu (ARAKI_SADECE_GERCEK=1 ile):
    ///   Unity.exe -batchmode -quit -projectPath unity -executeMethod ARaki.EditorTools.KonumVeriUretici.Uret
    ///
    /// GERÇEK VERİ TANIMLARI (mobilya çarpıştırıcıları)
    /// Sahne dosyasında her mobilyanın tek bir kutu çarpıştırıcısı (modelin tüm sınır kutusu) vardır. Üretici yalnızca bellekte, sahne
    /// açıldıktan sonra her mobilya parçasının çizim ağından (MeshFilter) dışbükey olmayan bir MeshCollider oluşturur.
    /// Yol benzetimi, çarpışma doğrulaması ve başlangıç noktası seçimi (OpenSpot) ESKİ kutu çarpıştırıcılarıyla yapılır, bu yüzden
    /// yollar ve çizilmiş panoramalar aynı kalır. Gerçek veri (harita, mesafeler) ağ çarpıştırıcılarıyla üretilir. Duvarlar, kapı
    /// çerçeveleri ve zemin (ilkel küpler) olduğu gibi kalır.
    ///  - gercek_harita.png: 0.05 m hücre; 0.02-0.30 m yükseklik aralığında herhangi bir çarpıştırıcı hücre kutusuyla kesişiyorsa DOLU (0),
    ///    değilse BOŞ (255); ev sınırı dışı 128. Masa ayağı dolu, masa altı boş. gercek_harita_kutu.png: eski (kutu) tanım.
    ///  - gercek_mesafe_zemin.csv: kamera zemin noktasından 0.03 m yükseklikte yatay ışın, azimut k (robot-göreli, 0 = ön, saat yönü) derece.
    ///  - gercek_mesafe_duvar.csv: aynı, 2.3 m yükseklikte. En çok 20 m.
    ///  - gercek_mesafe_gorunen_zemin.csv: GÖRÜNEN ZEMİN SINIRI. Mercek L = kameranın dünya konumu (zeminden 0.255 m). Azimut k için
    ///    (k = 1 derecelik aralığın merkezi, diğer iki dosyayla aynı azimut dizilimi), yer noktaları P(d) (zeminden 0.005 m yukarıda),
    ///    d = 0.10, 0.12, ... 20.00 m. L'den P(d)'ye çizgi (arka yüzler dahil, tetikleyiciler hariç) herhangi bir çarpıştırıcıya (mobilya
    ///    ağı, duvar, kapı çerçevesi) çarparsa P(d) görünmez; ilk görünmeyen d sınırdır (hiç yoksa 20). Mercek yüksekliğinin üstündeki masa
    ///    tablası altındaki zemini gizlemez; ayak ya da alçak koltuk gizler. Robot gövdesi hesaba katılmaz (panoramada zaten maskeli).
    ///    Panoramada sınır pikseli: sütun = W/2 + k/360*W, satır = H/2 + atan(0.255/d)/pi*H.
    /// </summary>
    public static class KonumVeriUretici
    {
        const string ScenePath = "Assets/_Project/Scenes/DriveSim.unity";
        const double Dt = 1.0 / 15.0;
        const int SubSteps = 5;
        const float RobotRadius = 0.17f;
        const double PwmMaxSpeed = 0.55;       // PWM 255 ~ bu doğrusal hız (m/sn)
        const double PwmMaxTurn = 197.0;       // PWM 255 ~ bu dönüş hızı (derece/sn), MockRobotLink'in iz genişliği ve verimiyle
        const float CellSize = 0.05f;
        const float CapMeters = 20f;
        const int Azimuths = 360;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ------------------------------------------------------------------ ayarlar

        static string Env(string k) => Environment.GetEnvironmentVariable(k);
        static double EnvD(string k, double d) => Env(k) != null ? double.Parse(Env(k), Inv) : d;

        static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string OutRoot => Env("ARAKI_KAYIT_KOK") ?? Path.Combine(RepoRoot, "kayitlar", "sim");

        // ------------------------------------------------------------------ sahne

        static Transform robot;
        static MockRobotLink link;
        static Sim360Camera cam360;
        static Vector3 camLocal;
        static int panoWidth = 1536, panoHeight = 768;
        static RenderTexture outRt;
        static Texture2D outTex;

        static bool gtOnly;
        static readonly List<BoxCollider> furnBoxes = new List<BoxCollider>();
        static readonly List<MeshCollider> furnMeshes = new List<MeshCollider>();
        static readonly List<string> meshReport = new List<string>();

        static void OpenScene(bool meshColliders = true)
        {
            EditorSceneManager.OpenScene(ScenePath);
            ThemeCatalog.Set(ThemeId.Home, false);
            var skin = Object.FindFirstObjectByType<ThemeSkin>();
            if (skin != null) skin.Apply();

            robot = GameObject.Find("Robot").transform;
            link = robot.GetComponent<MockRobotLink>();
            link.Initialize();
            cam360 = Object.FindFirstObjectByType<Sim360Camera>();
            camLocal = cam360.transform.localPosition;
            cam360.GetComponent<Camera>().farClipPlane = 30f;   // yalnız bellekte; sahne dosyası değişmez

            // Panoramada yalnız gerçek ev olmalı: robot gövdesi ve tüm AR/oyun nesneleri (ViewerRig altında) gizlenir.
            foreach (var r in robot.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            var rig = GameObject.Find("ViewerRig");
            if (rig != null) rig.SetActive(false);
            foreach (var l in robot.GetComponentsInChildren<Light>(true)) l.enabled = false;

            if (meshColliders) BuildMeshColliders();
            SetMode(false);
            Physics.SyncTransforms();
        }

        /// <summary>
        /// Mobilya = alt öğelerinde MeshFilter olan, kendisinde MeshFilter olmayan, kutu çarpıştırıcılı nesne (ilkel küpler duvar/zemin/kapı).
        /// Her çizilen alt ağ için MeshCollider eklenir (başlangıçta etkisiz); yalnızca bellek içi, sahne kaydedilmez.
        /// </summary>
        static void BuildMeshColliders()
        {
            furnBoxes.Clear(); furnMeshes.Clear(); meshReport.Clear();
            int ok = 0, bad = 0, tris = 0;
            foreach (var bc in Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
            {
                var go = bc.gameObject;
                if (go.GetComponent<MeshFilter>() != null) continue;
                if (robot != null && go.transform.IsChildOf(robot)) continue;
                var mfs = go.GetComponentsInChildren<MeshFilter>(false);
                var made = new List<MeshCollider>();
                string why = "";
                foreach (var mf in mfs)
                {
                    var m = mf.sharedMesh;
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (m == null) { why += " [bos ag: " + mf.name + "]"; continue; }
                    if (mr == null || !mr.enabled) continue;
                    if (mf.GetComponent<Collider>() != null) continue;
                    if (!m.isReadable) why += " [okunamaz ag (isReadable=false): " + m.name + "]";
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = m;
                    mc.convex = false;
                    mc.enabled = false;
                    made.Add(mc);
                    tris += (int)(m.GetIndexCount(0) / 3);
                }
                bool failed = false;
                if (made.Count == 0) { failed = true; why += " [ag yok]"; }
                else
                {
                    // doğrulama: dışarıdan merkeze atılan ışınlardan en az biri bu mobilyanın bir ağına çarpmalı
                    foreach (var mc in made) mc.enabled = true;
                    bc.enabled = false;
                    Physics.SyncTransforms();
                    var c = bc.bounds.center; float r = bc.bounds.extents.magnitude + 1f;
                    bool hit = false;
                    foreach (var dir in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back, Vector3.down, Vector3.up })
                    {
                        var o = c - dir * r;
                        foreach (var mc in made) if (mc.Raycast(new Ray(o, dir), out _, 2 * r)) { hit = true; break; }
                        if (hit) break;
                    }
                    foreach (var mc in made) mc.enabled = false;
                    bc.enabled = true;
                    if (!hit) { failed = true; why += " [ag carpistirici isinla dogrulanamadi]"; }
                }
                if (failed)
                {
                    foreach (var mc in made) Object.DestroyImmediate(mc);
                    bad++;
                    meshReport.Add("SORUN " + go.name + why + " -> eski kutu carpistirici korunuyor");
                    continue;
                }
                furnBoxes.Add(bc);
                furnMeshes.AddRange(made);
                ok++;
                if (why != "") meshReport.Add("UYARI " + go.name + why);
            }
            Debug.Log($"[KonumVeri] mobilya ag carpistiricilari: {ok} mobilya ({furnMeshes.Count} ag, {tris} ucgen), sorunlu {bad}");
            foreach (var l in meshReport) Debug.Log("[KonumVeri] " + l);
        }

        /// <summary>mesh=true: mobilyada ağ çarpıştırıcı etkin (gerçek veri); false: eski kutu (yol benzetimi).</summary>
        static void SetMode(bool mesh)
        {
            foreach (var b in furnBoxes) if (b != null) b.enabled = !mesh;
            foreach (var m in furnMeshes) if (m != null) m.enabled = mesh;
            Physics.SyncTransforms();
        }

        static void PrepareRender(int width)
        {
            panoWidth = width;
            panoHeight = width / 2;
            cam360.EnsureTargets();
            outRt = new RenderTexture(panoWidth, panoHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { filterMode = FilterMode.Bilinear };
            outRt.Create();
            outTex = new Texture2D(panoWidth, panoHeight, TextureFormat.RGB24, false);

            // Gölgelendirici varyantları arka planda derlenir; hazır olana kadar yer tutucu çizilir.
            link.Teleport(HouseLayout.BasePosition, HouseLayout.BaseYaw);
            Physics.SyncTransforms();
            for (int i = 0; i < 120; i++)
            {
                cam360.Capture();
                System.Threading.Thread.Sleep(250);
                if (!ShaderUtil.anythingCompiling && i >= 3) break;
            }
        }

        /// <summary>
        /// Robotu yerleştirir ve robot-göreli panoramayı outTex'e okur. Unity'nin küp haritası dünyaya sabittir
        /// (ortada +Z kuzey, sağa doğru doğu); robotun yönü kadar yatay kaydırılır: ortadaki sütun robotun önü olur.
        /// 3072 genişlikte çekilip 2:1 küçültülürken kaydırma tek geçişte yapılır (kesirli piksel kaymasında yumuşatma kalmaz).
        /// </summary>
        static void RenderPano(Vector3 robotPos, float yaw)
        {
            link.Teleport(robotPos, yaw);
            Physics.SyncTransforms();
            cam360.Capture();
            float shift = ShiftSign * Mathf.Repeat(yaw, 360f) / 360f;
            Graphics.Blit(cam360.Equirect, outRt, Vector2.one, new Vector2(shift, 0f));
            var prev = RenderTexture.active;
            RenderTexture.active = outRt;
            outTex.ReadPixels(new Rect(0, 0, panoWidth, panoHeight), 0, 0);
            RenderTexture.active = prev;
        }

        // Sütun u (robot-göreli) = dünya-sabit görüntüde u + yaw/360: dönülürken dünya nesneleri sola kayar.
        const float ShiftSign = 1f;

        static Vector3 CamGround => new Vector3(cam360.transform.position.x, 0f, cam360.transform.position.z);

        // ------------------------------------------------------------------ çarpışma ve harita

        static bool Blocked(double x, double z) =>
            Physics.CheckCapsule(new Vector3((float)x, 0.10f, (float)z), new Vector3((float)x, 0.28f, (float)z), RobotRadius, ~0, QueryTriggerInteraction.Ignore);

        sealed class Grid
        {
            public int W, H;
            public float X0, Y0;                    // sol-alt köşe (dünya x, z), harita sınırlarının dışına 0.25 m pay dahil
            public byte[] Px;                       // satır 0 = kuzey (üst), sütun 0 = batı; 0 dolu, 255 boş, 128 ev dışı
            public bool Occupied(int col, int row) => Px[row * W + col] == 0;
            public Vector2 CellCenter(int col, int row) => new Vector2(X0 + (col + 0.5f) * CellSize, Y0 + (H - 1 - row + 0.5f) * CellSize);
            public bool ToCell(double x, double z, out int col, out int row)
            {
                col = (int)Math.Floor((x - X0) / CellSize);
                row = H - 1 - (int)Math.Floor((z - Y0) / CellSize);
                return col >= 0 && col < W && row >= 0 && row < H;
            }
        }

        static Grid BuildGrid()
        {
            const float margin = 0.25f;
            var b = HouseLayout.Bounds;
            var g = new Grid
            {
                X0 = b.xMin - margin,
                Y0 = b.yMin - margin,
                W = Mathf.CeilToInt((b.width + 2 * margin) / CellSize),
                H = Mathf.CeilToInt((b.height + 2 * margin) / CellSize),
            };
            g.Px = new byte[g.W * g.H];
            for (int row = 0; row < g.H; row++)
                for (int col = 0; col < g.W; col++)
                {
                    var c = g.CellCenter(col, row);
                    byte v;
                    if (!b.Contains(c)) v = 128;
                    else
                    {
                        // 0.02-0.30 m yükseklik aralığı: robotun (25 cm) önünü kesen her şey
                        bool hit = Physics.CheckBox(new Vector3(c.x, 0.16f, c.y), new Vector3(CellSize * 0.5f, 0.14f, CellSize * 0.5f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                        v = hit ? (byte)0 : (byte)255;
                    }
                    g.Px[row * g.W + col] = v;
                }
            return g;
        }

        static byte[] MapPng(Grid g)
        {
            var tex = new Texture2D(g.W, g.H, TextureFormat.RGB24, false);
            var pix = new Color32[g.W * g.H];
            for (int row = 0; row < g.H; row++)
                for (int col = 0; col < g.W; col++)
                {
                    byte v = g.Px[row * g.W + col];
                    pix[(g.H - 1 - row) * g.W + col] = new Color32(v, v, v, 255);   // Texture2D satır 0 = alt; dosyada üst = kuzey
                }
            tex.SetPixels32(pix);
            var bytes = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            return bytes;
        }

        /// <summary>Gerçek haritayı (ağ çarpıştırıcılar) ve eski kutu haritasını OutRoot'a yazar; kutu haritası eski dosyayla bayt bayt karşılaştırılır.</summary>
        static Grid WriteMaps(Grid boxGrid)
        {
            SetMode(true);
            var meshGrid = BuildGrid();
            SetMode(false);
            string oldPath = Path.Combine(OutRoot, "gercek_harita.png");
            byte[] boxPng = MapPng(boxGrid);
            string kutuPath = Path.Combine(OutRoot, "gercek_harita_kutu.png");
            if (File.Exists(oldPath) && !File.Exists(kutuPath))
            {
                bool same = System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(oldPath), boxPng);
                Debug.Log($"[KonumVeri] eski gercek_harita.png, kutu carpistiricilarla yeniden uretilenle bayt bayt ayni mi: {same}");
            }
            Directory.CreateDirectory(OutRoot);
            File.WriteAllBytes(kutuPath, boxPng);
            WriteMap(meshGrid, OutRoot);
            int d2e = 0, e2d = 0;
            for (int i = 0; i < meshGrid.Px.Length; i++)
            {
                if (boxGrid.Px[i] == 0 && meshGrid.Px[i] == 255) d2e++;
                if (boxGrid.Px[i] == 255 && meshGrid.Px[i] == 0) e2d++;
            }
            Debug.Log($"[KonumVeri] harita: kutu -> ag: DOLU->BOS {d2e} hucre, BOS->DOLU {e2d} hucre");
            return meshGrid;
        }

        static void WriteMap(Grid g, string folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "gercek_harita.png"), MapPng(g));
            var sb = new StringBuilder();
            sb.Append("{\"hucre_m\":").Append(CellSize.ToString("0.###", Inv));
            sb.Append(",\"x0_m\":").Append(g.X0.ToString("0.###", Inv));
            sb.Append(",\"y0_m\":").Append(g.Y0.ToString("0.###", Inv));
            sb.Append(",\"genislik\":").Append(g.W).Append(",\"yukseklik\":").Append(g.H);
            sb.Append(",\"satir_yonu\":\"Piksel (sutun c, satir r), satir 0 = en ust = kuzey. Hucre merkezi: x_m = x0_m + (c + 0.5) * hucre_m, y_m = y0_m + (yukseklik - 1 - r + 0.5) * hucre_m. x dogu, y kuzey (Unity z).\"");
            sb.Append(",\"piksel\":\"0 = dolu (0.02-0.30 m arasinda bir cisim var; mobilya cisimleri cizim agidir, masa ayagi dolu masa alti bos; eski kutu tanimi gercek_harita_kutu.png), 255 = bos (ev ici), 128 = ev siniri disi\"");
            sb.Append(",\"ev_sinirlari_m\":{\"x_min\":").Append(HouseLayout.Bounds.xMin.ToString("0.###", Inv));
            sb.Append(",\"x_max\":").Append(HouseLayout.Bounds.xMax.ToString("0.###", Inv));
            sb.Append(",\"y_min\":").Append(HouseLayout.Bounds.yMin.ToString("0.###", Inv));
            sb.Append(",\"y_max\":").Append(HouseLayout.Bounds.yMax.ToString("0.###", Inv)).Append("}}");
            File.WriteAllText(Path.Combine(folder, "gercek_harita.json"), sb.ToString());
        }

        /// <summary>Engel haritasını (robot yarıçapıyla şişirilmiş) metin olarak yazar; yol tasarlamak içindir.</summary>
        public static void HaritaYaz()
        {
            OpenScene();
            var g = BuildGrid();                    // kutu çarpıştırıcılar: yol tasarımı için
            WriteMaps(g);
            string path = Env("ARAKI_ASCII") ?? Path.Combine(OutRoot, "engel_haritasi.txt");
            var sb = new StringBuilder();
            const float step = 0.1f;
            var b = HouseLayout.Bounds;
            sb.AppendLine("# '#': robot merkezi giremez (cisim ya da yaricap 0.17 icinde), '.': serbest, ' ': ev disi. Her satir 0.1 m; sol sayi y (kuzey), ustte x.");
            var hdr = new StringBuilder("       ");
            for (float x = b.xMin; x < b.xMax; x += step)
            {
                int ix = Mathf.RoundToInt((x - b.xMin) / step);
                hdr.Append(ix % 10 == 0 ? ((int)Mathf.Round(x)).ToString().PadLeft(2).Substring(0, 1) : " ");
            }
            sb.AppendLine(hdr.ToString());
            for (float z = b.yMax - step * 0.5f; z > b.yMin; z -= step)
            {
                sb.Append(z.ToString("0.0", Inv).PadLeft(6)).Append(' ');
                for (float x = b.xMin + step * 0.5f; x < b.xMax; x += step)
                    sb.Append(Blocked(x, z) ? '#' : '.');
                sb.AppendLine();
            }
            // açık noktalar
            sb.AppendLine("# En acik nokta (engele uzaklik, m):");
            foreach (var r in HouseLayout.Rooms)
            {
                var p = OpenSpot(g, r, out float clear);
                sb.AppendLine($"{r.Id}: x={p.x:0.00} y={p.y:0.00} bosluk={clear:0.00}");
            }
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[KonumVeri] engel haritasi: " + path);
        }

        /// <summary>Odadaki en açık nokta: en yakın dolu hücreye en uzak (oda merkezine hafif yakınlık tercihiyle).</summary>
        static Vector2 OpenSpot(Grid g, RoomInfo room, out float clearance)
        {
            var rect = room.Rect;
            float best = -1e9f; var bestP = rect.center; clearance = 0f;
            for (float x = rect.xMin + 0.4f; x < rect.xMax - 0.4f; x += 0.1f)
                for (float z = rect.yMin + 0.4f; z < rect.yMax - 0.4f; z += 0.1f)
                {
                    float d = Clearance(g, x, z, 2.5f);
                    float score = d - 0.03f * Vector2.Distance(new Vector2(x, z), rect.center);
                    if (score > best) { best = score; bestP = new Vector2(x, z); clearance = d; }
                }
            return bestP;
        }

        static float Clearance(Grid g, float x, float z, float max)
        {
            if (!g.ToCell(x, z, out int c0, out int r0)) return 0f;
            int rad = Mathf.CeilToInt(max / CellSize);
            float best = max;
            for (int r = Math.Max(0, r0 - rad); r <= Math.Min(g.H - 1, r0 + rad); r++)
                for (int c = Math.Max(0, c0 - rad); c <= Math.Min(g.W - 1, c0 + rad); c++)
                {
                    if (g.Px[r * g.W + c] != 0) continue;
                    var p = g.CellCenter(c, r);
                    float d = Mathf.Sqrt((p.x - x) * (p.x - x) + (p.y - z) * (p.y - z)) - CellSize * 0.5f;
                    if (d < best) best = d;
                }
            return best;
        }

        // ------------------------------------------------------------------ gerçek mesafe ışınları

        static readonly float[] ray = new float[Azimuths];

        static void CastRing(Vector3 origin, float yaw, float height, float[] dst)
        {
            for (int k = 0; k < Azimuths; k++)
            {
                float a = (yaw + k) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                dst[k] = Physics.Raycast(new Vector3(origin.x, height, origin.z), dir, out var hit, CapMeters, ~0, QueryTriggerInteraction.Ignore)
                    ? hit.distance : CapMeters;
            }
        }

        const float FloorPointY = 0.005f, VisStart = 0.10f, VisStep = 0.02f;

        /// <summary>
        /// Görünen zemin sınırı (tanım: dosya başlığı). Mercekten zemin noktasına çizgiler RaycastCommand ile toplu atılır;
        /// her azimutta 100 adımlık (2 m) parçalar, sınırı bulunmayan azimutlar için sürdürülür.
        /// </summary>
        static void VisibleFloor(Vector3 lens, float yaw, float[] dst)
        {
            int maxSteps = Mathf.RoundToInt((CapMeters - VisStart) / VisStep) + 1;   // d = 0.10 .. 20.00
            const int chunk = 100;
            var done = new bool[Azimuths];
            for (int k = 0; k < Azimuths; k++) dst[k] = CapMeters;
            var dirs = new Vector3[Azimuths];
            for (int k = 0; k < Azimuths; k++) { float a = (yaw + k) * Mathf.Deg2Rad; dirs[k] = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)); }
            var qp = new QueryParameters(~0, false, QueryTriggerInteraction.Ignore, true);
            var active = new List<int>(Azimuths);
            for (int k = 0; k < Azimuths; k++) active.Add(k);
            for (int s = 0; s < maxSteps && active.Count > 0; s += chunk)
            {
                int n = Math.Min(chunk, maxSteps - s);
                var cmds = new Unity.Collections.NativeArray<RaycastCommand>(active.Count * n, Unity.Collections.Allocator.TempJob);
                var res = new Unity.Collections.NativeArray<RaycastHit>(active.Count * n, Unity.Collections.Allocator.TempJob);
                for (int ai = 0; ai < active.Count; ai++)
                {
                    var dir = dirs[active[ai]];
                    for (int j = 0; j < n; j++)
                    {
                        float dd = VisStart + (s + j) * VisStep;
                        var p = new Vector3(lens.x + dir.x * dd, FloorPointY, lens.z + dir.z * dd);
                        var v = p - lens; float len = v.magnitude;
                        cmds[ai * n + j] = new RaycastCommand(lens, v / len, qp, len);
                    }
                }
                RaycastCommand.ScheduleBatch(cmds, res, 256, 1).Complete();
                var next = new List<int>(active.Count);
                for (int ai = 0; ai < active.Count; ai++)
                {
                    int k = active[ai]; bool found = false;
                    for (int j = 0; j < n; j++)
                        if (res[ai * n + j].distance > 0f) { dst[k] = VisStart + (s + j) * VisStep; found = true; break; }
                    if (!found) next.Add(k);
                }
                active = next;
                cmds.Dispose(); res.Dispose();
            }
        }

        static void AppendRing(StringBuilder sb, int frame, float[] d)
        {
            sb.Append(frame);
            for (int k = 0; k < d.Length; k++) sb.Append(',').Append(d[k].ToString("0.###", Inv));
            sb.Append('\n');
        }

        // ------------------------------------------------------------------ hareket benzetimi

        struct Cmd { public double F, W; }      // nominal ileri hız (m/sn), nominal dönüş (derece/sn, saat yönü +)

        /// <summary>Kaymalı gerçek hareket: gerçek hız/dönüş komuttan sapar (yavaş değişen hata + sabit sapma + motor gecikmesi).</summary>
        sealed class Sim
        {
            public double X, Z, Yaw;                // robot orijini; Yaw kuzeyden saat yönünde derece (sürekli, sarmalanmamış)
            public double SlipF, SlipR;
            public double SpeedLimit = 0.15;
            public double DriftDegPerM;
            double v, w;
            readonly System.Random rng;
            const double Tau = 6.0, Sigma = 0.06, MotorTauV = 0.12, MotorTauW = 0.10;

            public Sim(int seed, double x, double z, double yaw, double drift)
            {
                rng = new System.Random(seed);
                X = x; Z = z; Yaw = yaw; DriftDegPerM = drift;
                SlipF = 0.4 * SpeedLimit * Gauss(); SlipR = 0.4 * SpeedLimit * Gauss();
                SlipF = Clamp(SlipF); SlipR = Clamp(SlipR);
            }

            public double Gauss()
            {
                double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
                return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            }

            double Clamp(double s) => Math.Max(-SpeedLimit, Math.Min(SpeedLimit, s));

            public void Frame(Cmd c)
            {
                double h = Dt / SubSteps;
                for (int i = 0; i < SubSteps; i++)
                {
                    SlipF = Clamp(SlipF - SlipF * h / Tau + Sigma * Math.Sqrt(h) * Gauss());
                    SlipR = Clamp(SlipR - SlipR * h / Tau + Sigma * Math.Sqrt(h) * Gauss());
                    double vt = c.F * (1 + SlipF);
                    double wt = c.W * (1 + SlipR) + DriftDegPerM * v;
                    v += (vt - v) * (1 - Math.Exp(-h / MotorTauV));
                    w += (wt - w) * (1 - Math.Exp(-h / MotorTauW));
                    double yawMid = (Yaw + w * h * 0.5) * Math.PI / 180.0;
                    X += v * Math.Sin(yawMid) * h;
                    Z += v * Math.Cos(yawMid) * h;
                    Yaw += w * h;
                }
            }
        }

        /// <summary>Operatör: komut dizileri üretir. Git(...) "ekrana bakarak sür" gibi gerçek konuma bakıp düzeltir.</summary>
        sealed class Op
        {
            public Sim S;
            public double V, W, Ramp = 0.3;
            public double MaxFrames = double.MaxValue;

            static IEnumerable<Cmd> Zero(int n) { for (int i = 0; i < n; i++) yield return new Cmd(); }

            /// <summary>Hız profili: rampalı yamuk, toplamı tam olarak total olacak şekilde ölçeklenir.</summary>
            static List<double> Profile(double total, double umax, double ramp)
            {
                var list = new List<double>();
                double D = Math.Abs(total);
                if (D < 1e-9) return list;
                double up = Math.Min(umax, D / ramp);
                double T = D / up + ramp;
                int n = Math.Max(1, (int)Math.Ceiling(T / Dt));
                double Tn = n * Dt, sum = 0;
                for (int i = 0; i < n; i++)
                {
                    double t = (i + 0.5) * Dt;
                    double s = Math.Max(0, Math.Min(1, Math.Min(t / ramp, (Tn - t) / ramp)));
                    list.Add(s); sum += s * Dt;
                }
                double k = D / sum * Math.Sign(total);
                for (int i = 0; i < n; i++) list[i] *= k;
                return list;
            }

            public IEnumerable<Cmd> Wait(double seconds) => Zero((int)Math.Round(seconds / Dt));
            public IEnumerable<Cmd> Drive(double meters) { foreach (var u in Profile(meters, V, Ramp)) yield return new Cmd { F = u }; }
            public IEnumerable<Cmd> Turn(double degrees) { foreach (var u in Profile(degrees, W, Ramp)) yield return new Cmd { W = u }; }

            public static double Wrap180(double a) { a %= 360.0; if (a > 180) a -= 360; if (a < -180) a += 360; return a; }

            /// <summary>
            /// Hedefe sür: operatör gerçek konumu (gürültülü) görür, gerekirse yön düzeltir, en çok `chunk` metrelik parçalarla ilerler.
            /// precise = dar geçit: küçük parçalar, sıkı yön toleransı, az gürültü.
            /// </summary>
            public IEnumerable<Cmd> GoTo(double x, double z, bool precise = false, double tolerance = 0.1)
            {
                double chunk = precise ? 1.0 : 2.0, aimTol = precise ? 2.0 : 8.0, posN = precise ? 0.01 : 0.02, yawN = precise ? 0.5 : 1.5;
                for (int iter = 0; iter < 80; iter++)
                {
                    double ex = S.X + posN * S.Gauss(), ez = S.Z + posN * S.Gauss(), ey = S.Yaw + yawN * S.Gauss();
                    double dx = x - ex, dz = z - ez, dist = Math.Sqrt(dx * dx + dz * dz);
                    if (dist < tolerance) yield break;
                    double err = Wrap180(Math.Atan2(dx, dz) * 180.0 / Math.PI - ey);
                    if (Math.Abs(err) > aimTol)
                    {
                        foreach (var c in Turn(err)) yield return c;
                        foreach (var c in Wait(0.15)) yield return c;
                        continue;
                    }
                    foreach (var c in Drive(Math.Min(dist, chunk))) yield return c;
                    foreach (var c in Wait(0.1)) yield return c;
                }
            }

            /// <summary>Verilen yöne (kuzeyden saat yönünde derece) dön; gerçek yöne bakarak düzeltir.</summary>
            public IEnumerable<Cmd> FaceTo(double yawDeg, double tol = 2.0, double noise = 0.7)
            {
                for (int iter = 0; iter < 8; iter++)
                {
                    double err = Wrap180(yawDeg - (S.Yaw + noise * S.Gauss()));
                    if (Math.Abs(err) <= tol) yield break;
                    foreach (var c in Turn(err)) yield return c;
                    foreach (var c in Wait(0.15)) yield return c;
                }
            }
        }

        sealed class Track
        {
            public string Name, Description;
            public int Seed;
            public Vector3 Start;                   // robot orijini (x, 0, z)
            public float StartYaw;
            public Func<Op, IEnumerable<Cmd>> Script;
        }

        sealed class Rec
        {
            public double T;                        // sn
            public double CamX, CamY, CamYaw;       // kamera yer noktası ve yön (0..360)
            public double RobX, RobZ, RobYaw;
            public int Left, Right;                 // bu kareden sonraki aralığın komutu (PWM)
        }

        static int Pwm(double v) => (int)Math.Round(Math.Max(-255, Math.Min(255, v)));

        static List<Track> Tracks(Grid g, double drift)
        {
            var list = new List<Track>();
            int seed = 11;
            Vector2 livingSpot = OpenSpot(g, HouseLayout.Living, out _);
            list.Add(new Track
            {
                Name = "yerinde_donus_salon", Seed = seed++, Start = new Vector3(livingSpot.x, 0, livingSpot.y), StartYaw = 0f,
                Description = "Salonda en acik noktada yerinde: 2 tam tur saat yonunde, 1 tam tur saat yonu tersine",
                Script = o => Concat(o.Wait(1), o.Turn(720), o.Wait(1), o.Turn(-360), o.Wait(1)),
            });
            var others = new (RoomInfo room, string name)[]
            {
                (HouseLayout.Hall, "koridor"), (HouseLayout.Bedroom, "yatak_odasi"), (HouseLayout.Bath, "banyo"),
                (HouseLayout.Kitchen, "mutfak"), (HouseLayout.Study, "calisma_odasi"),
            };
            foreach (var (room, name) in others)
            {
                var p = OpenSpot(g, room, out _);
                list.Add(new Track
                {
                    Name = "yerinde_donus_" + name, Seed = seed++, Start = new Vector3(p.x, 0, p.y), StartYaw = 0f,
                    Description = room.Name + " odasinda en acik noktada yerinde 1 tam tur saat yonunde",
                    Script = o => Concat(o.Wait(1), o.Turn(360), o.Wait(1)),
                });
            }
            list.AddRange(TrackDefs.Build(drift));
            return list;
        }

        static IEnumerable<Cmd> Concat(params IEnumerable<Cmd>[] parts)
        {
            foreach (var p in parts) foreach (var c in p) yield return c;
        }

        static class TrackDefs
        {
            /// <summary>Elle yazılmış yollar (engel_haritasi.txt üzerinde tasarlandı; koordinatlar robot orijini, metre).</summary>
            public static List<Track> Build(double drift)
            {
                var list = new List<Track>();

                // duz_git: koridorun batı yarısında (konsol ve raf öncesi, 0.9 m genişlik) 5.1 m. Daha uzun serbest çizgi (9.4 m) var
                // ama konsol ile raf arasında yalnız 0.2 m pay bırakıyor; sabit sapma yüzünden açık çevrimde geçilemez.
                // Başlangıç yönü sapmayı yarı yarıya telafi eder (hedefe nişan alınmış gibi): yol 0.1 m'lik bir yay olur.
                const double L = 5.1;
                double theta0 = drift * L * 0.5;
                list.Add(new Track
                {
                    Name = "duz_git", Seed = 21, Start = new Vector3(-4.65f, 0f, 0.05f), StartYaw = (float)(90.0 - theta0),
                    Description = "Koridorda 5.1 m ileri, 2 sn dur, 180 derece don, geri gel (acik cevrim komutlar)",
                    Script = o => Concat(o.Wait(1), o.Drive(L), o.Wait(2), o.Turn(180), o.Wait(0.3), o.FaceTo(270.0 - theta0, 1.5, 0.5), o.Wait(0.5), o.Drive(L), o.Wait(1)),
                });

                // salon_turu: salonun serbest bölgesinde dikdörtgen kapalı tur (ada gibi duran kanepe-sehpa-koltuk batı duvarına bitişik, çevresi dolanılamaz)
                var A = new Vector2(-4.0f, -1.1f); var B = new Vector2(-0.8f, -1.1f); 
                var C2 = new Vector2(-0.8f, -2.1f); var F = new Vector2(-4.0f, -2.1f);
                list.Add(new Track
                {
                    Name = "salon_turu", Seed = 31, Start = new Vector3(A.x, 0f, A.y), StartYaw = 90f,
                    Description = "Salonda dikdortgen kapali tur: A-B-C-F-A, baslangic noktasina doner",
                    Script = o => Concat(o.Wait(1), o.GoTo(B.x, B.y), o.GoTo(C2.x, C2.y), o.GoTo(F.x, F.y), o.GoTo(A.x, A.y), o.FaceTo(90.0), o.Wait(1)),
                });

                // ev_turu
                list.Add(new Track
                {
                    Name = "ev_turu", Seed = 41, Start = HouseLayout.BasePosition, StartYaw = HouseLayout.BaseYaw,
                    Description = "Ust -> salon acikligi -> koridor -> mutfak -> koridor -> calisma odasi -> koridor -> salon -> ust",
                    Script = o => Concat(
                        o.Wait(1),
                        o.GoTo(-1.8, 0.10), o.GoTo(2.8, 0.10, true),   // koridor, konsol-raf darbogazi x 0.6-2.6
                        o.GoTo(3.2, 0.4), o.GoTo(3.2, 0.95), o.GoTo(3.2, 0.4),   // mutfak
                        o.GoTo(3.6, 0.10), o.GoTo(3.6, -0.95), o.GoTo(4.2, -1.0), o.GoTo(3.6, -0.95), o.GoTo(3.6, 0.10),   // calisma odasi
                        o.GoTo(2.9, 0.10, true), o.GoTo(2.4, 0.10, true), o.GoTo(1.8, 0.10, true), o.GoTo(1.2, 0.10, true), o.GoTo(0.6, 0.10, true), o.GoTo(-0.2, 0.10, true), o.GoTo(-1.8, 0.10), o.GoTo(HouseLayout.BasePosition.x, HouseLayout.BasePosition.z),
                        o.FaceTo(HouseLayout.BaseYaw), o.Wait(1)),
                });
                return list;
            }
        }

        // ------------------------------------------------------------------ ana giriş

        public static void Uret()
        {
            try { UretInner(); }
            catch (Exception e)
            {
                Debug.LogError("[KonumVeri] HATA: " + e);
                EditorApplication.Exit(1);
            }
        }

        static void UretInner()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            OpenScene();
            bool dry = Env("ARAKI_KURU") == "1";
            double V = EnvD("ARAKI_HIZ", 0.40), W = EnvD("ARAKI_DONUS_HIZI", 60.0), drift = EnvD("ARAKI_SAPMA", 2.0);
            int maxFrames = (int)EnvD("ARAKI_KARE_SINIRI", int.MaxValue);
            int width = (int)EnvD("ARAKI_GENISLIK", 1536);
            string only = Env("ARAKI_YOLLAR") ?? "hepsi";

            gtOnly = Env("ARAKI_SADECE_GERCEK") == "1";
            var grid = BuildGrid();                 // kutu çarpıştırıcılar: yollar ve başlangıç noktaları bunlarla belirlenir (değişmez)
            WriteMaps(grid);
            if (Env("ARAKI_ASCII") != null) HaritaYaz();
            if (!dry && !gtOnly) PrepareRender(width);

            var tracks = Tracks(grid, drift);
            var wanted = new HashSet<string>(only.Split(',', StringSplitOptions.RemoveEmptyEntries));
            int total = 0, violations = 0;
            foreach (var t in tracks)
            {
                if (only != "hepsi" && !wanted.Contains(t.Name)) continue;
                int v = RunTrack(t, grid, V, W, drift, maxFrames, dry, out int frames);
                total += frames; violations += v;
            }
            Debug.Log($"[KonumVeri] BITTI: toplam {total} kare, carpisma ihlali {violations}, sure {sw.Elapsed.TotalMinutes:0.0} dk, cikti {OutRoot}");
            if (violations > 0) { Debug.LogError("[KonumVeri] carpisma ihlali var: yol noktalarini duzeltin"); EditorApplication.Exit(3); }
        }

        static int RunTrack(Track t, Grid grid, double V, double W, double drift, int maxFrames, bool dry, out int frameCount)
        {
            string folder = Path.Combine(OutRoot, t.Name);
            Directory.CreateDirectory(folder);
            var sim = new Sim(t.Seed, t.Start.x, t.Start.z, t.StartYaw, drift);
            var op = new Op { S = sim, V = V, W = W };
            var recs = new List<Rec>();
            var enumerator = t.Script(op).GetEnumerator();
            int violations = 0, firstBad = -1;
            Vector2 firstBadPos = default;
            double lastLeft = 0, lastRight = 0;

            Rec Snapshot(Cmd c)
            {
                double yaw = ((sim.Yaw % 360.0) + 360.0) % 360.0;
                double ry = sim.Yaw * Math.PI / 180.0;
                var r = new Rec
                {
                    T = recs.Count * Dt, RobX = sim.X, RobZ = sim.Z, RobYaw = yaw, CamYaw = yaw,
                    // kamera: robot orijininden yerel (camLocal.x sağ, camLocal.z ileri) kadar kayık
                    CamX = sim.X + Math.Sin(ry) * camLocal.z + Math.Cos(ry) * camLocal.x,
                    CamY = sim.Z + Math.Cos(ry) * camLocal.z - Math.Sin(ry) * camLocal.x,
                    Left = Pwm((c.F / PwmMaxSpeed + c.W / PwmMaxTurn) * 255.0),
                    Right = Pwm((c.F / PwmMaxSpeed - c.W / PwmMaxTurn) * 255.0),
                };
                if (Blocked(sim.X, sim.Z))
                {
                    violations++;
                    if (firstBad < 0) { firstBad = recs.Count; firstBadPos = new Vector2((float)sim.X, (float)sim.Z); }
                }
                return r;
            }

            while (recs.Count < maxFrames)
            {
                Cmd c = default;
                bool more = enumerator.MoveNext();
                if (more) c = enumerator.Current;
                recs.Add(Snapshot(c));
                if (!more) break;
                sim.Frame(c);
            }
            frameCount = recs.Count;
            double lengthM = 0; for (int i = 1; i < recs.Count; i++) lengthM += Math.Sqrt(Math.Pow(recs[i].RobX - recs[i - 1].RobX, 2) + Math.Pow(recs[i].RobZ - recs[i - 1].RobZ, 2));
            Debug.Log($"[KonumVeri] {t.Name}: {recs.Count} kare, {recs.Count * Dt:0.0} sn, yol {lengthM:0.0} m, ihlal {violations}"
                + (firstBad >= 0 ? $" (ilk ihlal kare {firstBad} konum {firstBadPos.x:0.00},{firstBadPos.y:0.00})" : "")
                + $", son: robot {sim.X:0.00},{sim.Z:0.00} yon {sim.Yaw:0}");

            if (gtOnly)
            {
                ComparePose(folder, recs);
                WriteGroundTruth(folder, recs);
                PatchInfo(folder);
                return violations;
            }
            WriteOverlay(grid, recs, Path.Combine(folder, "yol.png"), out double onFree);
            Debug.Log($"[KonumVeri] {t.Name}: kamera noktalarinin harita BOS hucresinde olma orani {onFree * 100:0.0}%");

            WriteTables(folder, recs, grid);
            WriteInfo(folder, t, recs, V, W, drift, sim, lengthM, violations, onFree);
            if (!dry) { RenderAll(folder, recs); PatchInfo(folder); }
            return violations;
        }

        static void WriteTables(string folder, List<Rec> recs, Grid grid)
        {
            var frames = new StringBuilder("kare,t_ms\n");
            var pos = new StringBuilder("kare,t_ms,x_m,y_m,yon_derece\n");
            var cmd = new StringBuilder("t_ms,sol,sag\n");
            for (int i = 0; i < recs.Count; i++)
            {
                var r = recs[i];
                long ms = (long)Math.Round(r.T * 1000.0);
                frames.Append(i).Append(',').Append(ms).Append('\n');
                pos.Append(i).Append(',').Append(ms).Append(',').Append(r.CamX.ToString("0.0000", Inv)).Append(',').Append(r.CamY.ToString("0.0000", Inv)).Append(',').Append(r.CamYaw.ToString("0.000", Inv)).Append('\n');
                cmd.Append(ms).Append(',').Append(r.Left).Append(',').Append(r.Right).Append('\n');
            }
            File.WriteAllText(Path.Combine(folder, "kareler.csv"), frames.ToString());
            File.WriteAllText(Path.Combine(folder, "gercek_konum.csv"), pos.ToString());
            File.WriteAllText(Path.Combine(folder, "komutlar.csv"), cmd.ToString());
            foreach (var f in new[] { "gercek_harita.png", "gercek_harita.json" })
            {
                string src = Path.Combine(OutRoot, f), dst = Path.Combine(folder, f);
                if (File.Exists(src) && Path.GetFullPath(src) != Path.GetFullPath(dst)) File.Copy(src, dst, true);
            }
        }

        static void WriteInfo(string folder, Track t, List<Rec> recs, double V, double W, double drift, Sim sim, double lengthM, int violations, double onFree)
        {
            string D(double d, string f = "0.####") => d.ToString(f, Inv);
            var sb = new StringBuilder();
            sb.Append("{\"bicim\":1,\"kaynak\":\"sim\",\"ad\":\"").Append(t.Name).Append("\"");
            sb.Append(",\"aciklama\":\"").Append(t.Description).Append("\"");
            sb.Append(",\"kamera\":{\"tur\":\"esdikdortgen\",\"genislik\":").Append(panoWidth).Append(",\"yukseklik\":").Append(panoHeight).Append(",\"fps\":15}");
            sb.Append(",\"kamera_yuksekligi_m\":").Append(D(camLocal.y));
            sb.Append(",\"kamera_ofseti_robot_m\":{\"ileri\":").Append(D(camLocal.z)).Append(",\"sag\":").Append(D(camLocal.x)).Append(",\"not\":\"gercek_konum.csv kameranin zemindeki izdusumunu verir; robot orijini kameradan 'ileri' eksi kadar onde (kamera orijinin arkasinda)\"}");
            sb.Append(",\"koordinat\":\"x doğu, y kuzey, metre; yön: kuzeyden saat yönünde derece\"");
            sb.Append(",\"panorama\":\"orta sütun robotun önü, sütunlar saat yönünde artar\"");
            sb.Append(",\"panorama_ayrinti\":\"3072x1536 cekilip 2:1 kucultuldu; ust satir yukari; robot-goreli (robot yonune gore kaydirilmis); gorunen: yalniz ev (robot govdesi, AR ve oyun nesneleri yok)\"");
            sb.Append(",\"yol\":\"").Append(t.Name).Append("\"");
            sb.Append(",\"kayma_modeli\":{");
            sb.Append("\"tohum\":").Append(t.Seed);
            sb.Append(",\"nominal_hiz_m_sn\":").Append(D(V)).Append(",\"nominal_donus_derece_sn\":").Append(D(W)).Append(",\"rampa_sn\":0.3");
            sb.Append(",\"hiz_hatasi\":{\"sinir\":").Append(D(sim.SpeedLimit)).Append(",\"tau_sn\":6,\"sigma\":0.06,\"not\":\"ileri hiz ve donus hizi icin ayri, yavas degisen rastgele yuruyus (Ornstein-Uhlenbeck), +-sinir ile kirpilir; gercek = komut * (1 + hata)\"}");
            sb.Append(",\"sabit_sapma_derece_m\":").Append(D(drift)).Append(",\"sapma_yonu\":\"saat yonu (saga), ileri gidilen metre basina\"");
            sb.Append(",\"motor_gecikmesi_sn\":{\"ileri\":0.12,\"donus\":0.10}");
            sb.Append(",\"komut_donusumu\":{\"pwm_255_dogrusal_hiz_m_sn\":").Append(D(PwmMaxSpeed)).Append(",\"pwm_255_donus_derece_sn\":").Append(D(PwmMaxTurn)).Append(",\"not\":\"komutlar.csv nominal degerlerdir; sol = ileri + donus, sag = ileri - donus; donus saat yonu +\"}");
            sb.Append("}");
            sb.Append(",\"robot_yaricapi_m\":").Append(D(RobotRadius, "0.00"));
            sb.Append(",\"harita\":\"gercek_harita.png\"");
            sb.Append(",\"kare_sayisi\":").Append(recs.Count).Append(",\"sure_sn\":").Append(D(recs.Count * Dt, "0.0")).Append(",\"yol_uzunlugu_m\":").Append(D(lengthM, "0.00"));
            sb.Append(",\"carpisma_ihlali\":").Append(violations).Append(",\"bos_hucrede_oran\":").Append(D(onFree, "0.000"));
            sb.Append(",\"mesafe\":{\"zemin_yuksekligi_m\":0.03,\"duvar_yuksekligi_m\":2.3,\"tavan_m\":").Append(D(CapMeters, "0.0")).Append(",\"yon\":\"sutun k = robot-goreli azimut k derece (0 = on, saat yonunde artar)\"}");
            sb.Append("}");
            File.WriteAllText(Path.Combine(folder, "bilgi.json"), sb.ToString());
        }

        // ------------------------------------------------------------------ çıktı: kareler + ışınlar

        static void RenderAll(string folder, List<Rec> recs)
        {
            string frameDir = Path.Combine(folder, "kareler");
            Directory.CreateDirectory(frameDir);
            for (int i = 0; i < recs.Count; i++)
            {
                var r = recs[i];
                RenderPano(new Vector3((float)r.RobX, 0f, (float)r.RobZ), (float)r.RobYaw);
                File.WriteAllBytes(Path.Combine(frameDir, i.ToString("000000") + ".jpg"), outTex.EncodeToJPG(95));
                if (i % 200 == 0) Debug.Log($"[KonumVeri] {Path.GetFileName(folder)}: kare {i}/{recs.Count}");
            }
            WriteGroundTruth(folder, recs);
        }

        static readonly string[] GtFiles = { "gercek_harita.png", "gercek_harita.json", "gercek_harita_kutu.png", "gercek_mesafe_zemin.csv", "gercek_mesafe_duvar.csv", "gercek_mesafe_gorunen_zemin.csv" };

        /// <summary>Ağ çarpıştırıcılarla zemin/duvar/görünen zemin mesafelerini yazar; gerçek veri dosyalarını kayitlar/theta/&lt;ad&gt;/ klasörüne de kopyalar.</summary>
        static void WriteGroundTruth(string folder, List<Rec> recs)
        {
            SetMode(true);
            var floorCsv = new StringBuilder();
            var wallCsv = new StringBuilder();
            var visCsv = new StringBuilder();
            var d = new float[Azimuths];
            for (int i = 0; i < recs.Count; i++)
            {
                var r = recs[i];
                link.Teleport(new Vector3((float)r.RobX, 0f, (float)r.RobZ), (float)r.RobYaw);
                Physics.SyncTransforms();
                var cg = CamGround;
                CastRing(cg, (float)r.RobYaw, 0.03f, d); AppendRing(floorCsv, i, d);
                CastRing(cg, (float)r.RobYaw, 2.3f, d); AppendRing(wallCsv, i, d);
                VisibleFloor(cam360.transform.position, (float)r.RobYaw, d); AppendRing(visCsv, i, d);
                if (i % 200 == 0) Debug.Log($"[KonumVeri] {Path.GetFileName(folder)}: gercek veri kare {i}/{recs.Count}");
            }
            SetMode(false);
            File.WriteAllText(Path.Combine(folder, "gercek_mesafe_zemin.csv"), floorCsv.ToString());
            File.WriteAllText(Path.Combine(folder, "gercek_mesafe_duvar.csv"), wallCsv.ToString());
            File.WriteAllText(Path.Combine(folder, "gercek_mesafe_gorunen_zemin.csv"), visCsv.ToString());
            foreach (var f in new[] { "gercek_harita.png", "gercek_harita.json", "gercek_harita_kutu.png" })
            {
                string src = Path.Combine(OutRoot, f), dst = Path.Combine(folder, f);
                if (File.Exists(src) && Path.GetFullPath(src) != Path.GetFullPath(dst)) File.Copy(src, dst, true);
            }
            string theta = Path.GetFullPath(Path.Combine(OutRoot, "..", "theta", Path.GetFileName(folder)));
            if (Directory.Exists(theta))
                foreach (var f in GtFiles)
                {
                    string src = Path.Combine(folder, f);
                    if (File.Exists(src)) File.Copy(src, Path.Combine(theta, f), true);
                }
        }

        const string GtDefs = "{\"carpistirici\":\"Mobilya: cizim aginin (MeshCollider, disbukey olmayan) kendisi; masa ayagi dolu, masa alti bos. Duvar, kapi cercevesi ve zemin ilkel kutu. Yollar eski kutu carpistiricilarla uretildi, degismedi.\""
            + ",\"harita\":\"gercek_harita.png: 0.05 m hucre; 0.02-0.30 m yukseklikte herhangi bir carpistirici hucre kutusuyla kesisirse DOLU (0), degilse BOS (255), ev siniri disi 128. gercek_harita_kutu.png: ayni tanim, eski tek kutu carpistiricilarla.\""
            + ",\"mesafe_zemin\":\"gercek_mesafe_zemin.csv: kamera zemin noktasindan 0.03 m yukseklikte yatay isin; sutun k = robot-goreli azimut k derece (0 = on, saat yonu); en cok 20 m.\""
            + ",\"mesafe_duvar\":\"gercek_mesafe_duvar.csv: ayni, 2.3 m yukseklikte.\""
            + ",\"mesafe_gorunen_zemin\":\"gercek_mesafe_gorunen_zemin.csv: gorunen zemin siniri. Mercek L = kamera (zeminden 0.255 m). Azimut k (diger dosyalarla ayni dizilim) icin zemin noktasi P(d) (zeminden 0.005 m), d = 0.10, 0.12, ... 20.00 m; L'den P(d)'ye dogru cizgi herhangi bir carpistiriciya (mobilya agi, duvar, kapi cercevesi; arka yuzler dahil) carparsa P(d) gorunmez; ilk gorunmeyen d sinirdir, hic yoksa 20. Mercek yuksekliginin ustundeki masa tablasi altindaki zemini gizlemez; ayak ya da alcak koltuk gizler. Robot govdesi sayilmaz. Panoramada: sutun = W/2 + k/360*W, satir = H/2 + atan(0.255/d)/pi*H.\"}";

        /// <summary>bilgi.json'a (tek satırlık, üreticinin yazdığı biçim) "gercek_tanimlari" anahtarını ekler ya da yeniler.</summary>
        static void PatchInfo(string folder)
        {
            string p = Path.Combine(folder, "bilgi.json");
            if (!File.Exists(p)) return;
            string t = File.ReadAllText(p).TrimEnd();
            int i = t.IndexOf(",\"gercek_tanimlari\":", StringComparison.Ordinal);
            if (i >= 0) t = t.Substring(0, i) + "}";
            t = t.Substring(0, t.Length - 1) + ",\"gercek_tanimlari\":" + GtDefs + "}";
            File.WriteAllText(p, t);
        }

        /// <summary>Üretilen yolu dosyadaki gercek_konum.csv ile karşılaştırır (dosyaya yazmaz).</summary>
        static void ComparePose(string folder, List<Rec> recs)
        {
            var sb = new StringBuilder("kare,t_ms,x_m,y_m,yon_derece\n");
            for (int i = 0; i < recs.Count; i++)
            {
                var r = recs[i];
                long ms = (long)Math.Round(r.T * 1000.0);
                sb.Append(i).Append(',').Append(ms).Append(',').Append(r.CamX.ToString("0.0000", Inv)).Append(',').Append(r.CamY.ToString("0.0000", Inv)).Append(',').Append(r.CamYaw.ToString("0.000", Inv)).Append('\n');
            }
            string path = Path.Combine(folder, "gercek_konum.csv");
            string old = File.Exists(path) ? File.ReadAllText(path) : null;
            bool same = old == sb.ToString();
            double maxDiff = 0; int rows = -1;
            if (!same && old != null)
            {
                var a = old.Split('\n', StringSplitOptions.RemoveEmptyEntries); var b = sb.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                rows = Math.Min(a.Length, b.Length);
                for (int i = 1; i < rows; i++)
                {
                    var pa = a[i].Split(','); var pb = b[i].Split(',');
                    for (int j = 1; j < 5 && j < pa.Length && j < pb.Length; j++)
                        maxDiff = Math.Max(maxDiff, Math.Abs(double.Parse(pa[j], Inv) - double.Parse(pb[j], Inv)));
                }
            }
            Debug.Log($"[KonumVeri] KONUM {Path.GetFileName(folder)}: kare {recs.Count} (dosyada {(old == null ? -1 : old.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1)}), bayt bayt ayni={same}" + (same ? "" : $", en buyuk fark {maxDiff:0.########}"));
        }

        // ------------------------------------------------------------------ yol bindirmesi

        static void WriteOverlay(Grid g, List<Rec> recs, string path, out double onFree)
        {
            const int S = 4;
            int W = g.W * S, H = g.H * S;
            var pix = new Color32[W * H];
            for (int row = 0; row < g.H; row++)
                for (int col = 0; col < g.W; col++)
                {
                    byte v = g.Px[row * g.W + col];
                    var c = new Color32(v, v, v, 255);
                    for (int dy = 0; dy < S; dy++)
                        for (int dx = 0; dx < S; dx++)
                            pix[(H - 1 - (row * S + dy)) * W + col * S + dx] = c;
                }
            int onFreeCount = 0;
            Vector2 Px(double x, double z) => new Vector2((float)((x - g.X0) / CellSize * S), (float)((z - g.Y0) / CellSize * S));   // x sağa, y yukarı (dosya satırı = H-1-y)
            void Plot(int x, int y, Color32 col)
            {
                if (x < 0 || x >= W || y < 0 || y >= H) return;
                pix[y * W + x] = col;
            }
            void Line(Vector2 a, Vector2 b, Color32 col)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
                for (int i = 0; i <= n; i++) { var p = Vector2.Lerp(a, b, (float)i / n); Plot(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), col); }
            }
            Vector2 prev = default;
            for (int i = 0; i < recs.Count; i++)
            {
                var r = recs[i];
                if (g.ToCell(r.CamX, r.CamY, out int col, out int row) && g.Px[row * g.W + col] == 255) onFreeCount++;
                var p = Px(r.CamX, r.CamY);
                if (i > 0) Line(prev, p, new Color32(220, 30, 30, 255));
                prev = p;
                if (i % 15 == 0)    // saniyede bir yön çentiği
                {
                    double a = r.CamYaw * Math.PI / 180.0;
                    Line(p, p + new Vector2((float)Math.Sin(a), (float)Math.Cos(a)) * 6f, new Color32(30, 90, 230, 255));
                }
            }
            void Square(Vector2 c, Color32 col) { for (int dy = -4; dy <= 4; dy++) for (int dx = -4; dx <= 4; dx++) Plot(Mathf.RoundToInt(c.x) + dx, Mathf.RoundToInt(c.y) + dy, col); }
            if (recs.Count > 0)
            {
                Square(Px(recs[0].CamX, recs[0].CamY), new Color32(20, 190, 40, 255));
                Square(Px(recs[recs.Count - 1].CamX, recs[recs.Count - 1].CamY), new Color32(240, 200, 0, 255));
            }
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.SetPixels32(pix);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            onFree = recs.Count > 0 ? (double)onFreeCount / recs.Count : 1.0;
        }

        // ------------------------------------------------------------------ doğrulama çekimleri

        /// <summary>
        /// A ve B kontrolleri için panoramaları yazar: aynı konumda yaw 0 ve 90; kapıya bakan robot. Çıktı: ARAKI_KONTROL_KLASOR (varsayılan kayitlar/sim/_kontrol).
        /// </summary>
        public static void Kontrol()
        {
            try { KontrolInner(); }
            catch (Exception e) { Debug.LogError("[KonumVeri] HATA: " + e); EditorApplication.Exit(1); }
        }

        static void SavePano(string path) => File.WriteAllBytes(path, outTex.EncodeToPNG());

        static float[] ColumnProfile(int rowFrom, int rowTo)
        {
            var p = outTex.GetPixels32();
            var prof = new float[panoWidth];
            for (int x = 0; x < panoWidth; x++)
            {
                float s = 0;
                for (int y = rowFrom; y < rowTo; y++) { var c = p[y * panoWidth + x]; s += 0.299f * c.r + 0.587f * c.g + 0.114f * c.b; }
                prof[x] = s / (rowTo - rowFrom);
            }
            return prof;
        }

        static void KontrolInner()
        {
            OpenScene(false);
            PrepareRender(1536);
            string dir = Env("ARAKI_KONTROL_KLASOR") ?? Path.Combine(OutRoot, "_kontrol");
            Directory.CreateDirectory(dir);
            var inv = Inv;

            // A: aynı konum, yaw 0 ve yaw 90 (+ 180 ve 45)
            var spot = new Vector3(-3.4f, 0f, -2.0f);
            var profiles = new Dictionary<float, float[]>();
            foreach (float yaw in new[] { 0f, 45f, 90f, 180f })
            {
                RenderPano(spot, yaw);
                SavePano(Path.Combine(dir, $"a_yaw{(int)yaw}.png"));
                profiles[yaw] = ColumnProfile(panoHeight * 40 / 100, panoHeight * 60 / 100);
            }
            foreach (float yaw in new[] { 45f, 90f, 180f })
            {
                int bestShift = 0; double best = double.MinValue;
                var a = profiles[0f]; var b = profiles[yaw];
                for (int s = 0; s < panoWidth; s++)
                {
                    double acc = 0;
                    for (int x = 0; x < panoWidth; x++) acc -= Math.Abs(b[(x + s) % panoWidth] - a[x]);
                    if (acc > best) { best = acc; bestShift = s; }
                }
                int signed = bestShift > panoWidth / 2 ? bestShift - panoWidth : bestShift;
                // b[x + s] ~ a[x]: yaw 0'daki x sütunundaki nesne yaw'da x + s sütununda
                Debug.Log($"[KonumVeri] KONTROL A: yaw {yaw}: icerik {signed} sutun kaydi (beklenen {-yaw / 360f * panoWidth:0})");
            }

            // B: kapıya (salon-çalışma kapısı, x=1.8, z=-2.9) 1 m kala bak; kapı yaw 90'da ortada, yaw 0'da sağda olmalı
            var before = new Vector3(0.8f - camLocal.z * -1f * 0f, 0f, -2.9f);
            foreach (float yaw in new[] { 90f, 0f })
            {
                RenderPano(before, yaw);
                SavePano(Path.Combine(dir, $"b_kapi_yaw{(int)yaw}.png"));
            }
            // duvar mesafesi: ortadaki sütun (k=0) kapının önüne bakar
            link.Teleport(before, 90f); Physics.SyncTransforms();
            var dd = new float[Azimuths];
            CastRing(CamGround, 90f, 0.03f, dd);
            Debug.Log($"[KonumVeri] KONTROL B: yaw 90 zemin mesafesi k=0: {dd[0]:0.00} m (kapi acik: gecis uzaginda), k=-20: {dd[340]:0.00}, k=20: {dd[20]:0.00}");
            Debug.Log("[KonumVeri] kontrol dosyalari: " + dir);
        }
    }
}
