using UnityEngine;
using UnityEngine.Rendering;

namespace ArTarama
{
    /// <summary>
    /// Robotun hareketini telefonda, ekranda gösterilen 360 karenin kendisinden ölçer (23 Kasım: AR nesneleri görüntüyle
    /// birebir hareket etsin; robottan gelen konum ~0,3 sn geç ve 5 Hz):
    ///  - dönüş: ufuk bandı (enlem -5..+31, 0,5 derece/px) bir referans şeritle döngüsel korelasyon (robottaki teta.Pusula),
    ///  - ileri / yan: robotun çevresindeki zeminin kuşbakışı görüntüsü (2x2 m, 1 cm/px, kamera yerden 17 cm); dokulu
    ///    yamalar bir önceki karede aranır, dönüş çıkarıldıktan sonra yamaların ortanca kayması = robotun yer değiştirmesi
    ///    (robottaki zemin_izi.py ile aynı fikir; ölçek kamera yüksekliğinden, metre).
    /// Her yeni karede bir kez çağrılır; GPU'dan eşzamanlı okunur (ölçüm o kareyle aynı anda biter, gecikme yok).
    /// </summary>
    public class RobotGorselIz
    {
        const int BW = 720, BH = 24; const float PxDerece = BW / 360f;
        const int KN = 200; const float KR = 1.0f, KPx = KN / (2 * KR);
        const int Yama = 4, Ara = 8, IzGrid = 16;

        readonly Material mat;
        readonly RenderTexture bandRt, kusRt;
        readonly float[] bMaske, bPencere, kMaske;
        readonly float[] bCur = new float[BW * BH], bPrev = new float[BW * BH], bRef = new float[BW * BH];
        readonly float[] kCur = new float[KN * KN], kPrev = new float[KN * KN];
        bool bVar, kVar; float refYaw, relYaw, sonAdim;

        public float Yaw { get; private set; }               // toplam dönüş (derece, sağa +)
        public float PusulaGuven { get; private set; }
        public float AdimDonus { get; private set; }          // son karedeki dönüş (derece)
        public float AdimIleri { get; private set; }          // son karedeki ileri (m)
        public float AdimSag { get; private set; }            // son karedeki sağa (m)
        public int ZeminNokta { get; private set; }           // ortak harekete uyan yama sayısı
        public bool ZeminTuttu { get; private set; }
        public bool PusulaTuttu { get; private set; }
        /// <summary>Görüntü ölçümü motor tahminiyle çelişti, adım tahminden alındı (24 Kasım: video takılınca 20 derecelik
        /// atlamada yanlış / ters yönlü eşleşmeler 370 derecelik dönüşte 100 derece kaybettiriyordu).</summary>
        public bool TahminKullanildi { get; private set; }
        public int ReddedilenAdim { get; private set; }

        public RobotGorselIz(Material m)
        {
            mat = m;
            bandRt = new RenderTexture(BW, BH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Bilinear };
            kusRt = new RenderTexture(KN, KN, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Bilinear };
            bMaske = Maske("PusulaMaske", BW * BH); kMaske = Maske("ZeminMaske", KN * KN);
            bPencere = new float[BH];
            for (int y = 0; y < BH; y++) bPencere[y] = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * (y + 0.5f) / BH);
        }

        static float[] Maske(string ad, int n)
        {
            var ta = Resources.Load<TextAsset>(ad); var b = ta != null ? ta.bytes : null; var m = new float[n];
            for (int i = 0; i < n; i++) m[i] = b != null && b.Length == n ? (b[i] > 127 ? 1f : 0f) : 1f;
            return m;
        }

        public void Birak() { bandRt.Release(); kusRt.Release(); }

        static Color32[] Oku(RenderTexture rt)
        {
            var r = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
            r.WaitForCompletion();
            return r.hasError ? null : r.GetData<Color32>().ToArray();
        }

        /// <summary>Yeni kare (çift balık gözü doku) geldi: dönüş ve yer değiştirme adımını ölçer. tahmin: motor
        /// komutundan beklenen dönüş (derece, önceki işlenen kareden beri; takılmada büyür). Arama bunun çevresinde yapılır.</summary>
        public void Kare(Texture kare, float tahmin = 0f)
        {
            Graphics.Blit(kare, bandRt, mat, 1);
            Graphics.Blit(kare, kusRt, mat, 2);
            var bd = Oku(bandRt); var kd = Oku(kusRt);
            AdimDonus = 0f; AdimIleri = 0f; AdimSag = 0f; ZeminNokta = 0; ZeminTuttu = false; PusulaTuttu = false; TahminKullanildi = false;
            if (bd != null) Pusula(bd, tahmin);
            if (kd != null) Zemin(kd);
        }

        // ------------------------------------------------------------------ dönüş
        void Pusula(Color32[] d, float tahmin)
        {
            float e = 0f;
            for (int y = 0; y < BH; y++)
            {
                int o = y * BW;
                for (int x = 0; x < BW; x++)
                {
                    float g = d[o + (x + 1) % BW].r - d[o + (x + BW - 1) % BW].r;
                    float v = g * bMaske[o + x] * bPencere[y]; bCur[o + x] = v; e += v * v;
                }
            }
            if (e < 1f) return;
            float once = Yaw;
            if (!bVar) { System.Array.Copy(bCur, bRef, bCur.Length); System.Array.Copy(bCur, bPrev, bCur.Length); bVar = true; refYaw = Yaw; relYaw = 0; return; }
            // arama merkezi: önceki adım değil motor tahmini (dönüş başında / takılmadan sonra önceki adım yanıltır)
            float merkez = Mathf.Abs(tahmin) > 0.5f ? tahmin : sonAdim;
            Eslestir(bRef, bCur, (relYaw + merkez) * PxDerece, 24, out float s, out float c);
            bool refle = c >= 0.3f;
            if (!refle)
            {
                int aralik = Mathf.Clamp(70 + Mathf.RoundToInt(Mathf.Abs(merkez) * PxDerece * 0.5f), 70, 200);
                Eslestir(bPrev, bCur, merkez * PxDerece, aralik, out s, out c);
            }
            PusulaGuven = c;
            float adim = refle ? Mathf.DeltaAngle(0f, refYaw + s / PxDerece - once) : s / PxDerece;
            bool tuttu = refle || c >= 0.24f;
            // motorla çelişen ölçüm -> tahmini kullan: dönüş komutu varken ters yön ya da (büyük beklenen adımda) eşleşme yok.
            // Yalnız bu ikisi: "tahminden çok farklı" kuralı da denendi, 23 Kasım kayıtlarında kısa dokunuşlu dönüşlerde
            // tahmin karelerle tam hizalı olmadığı için 120 derece fazla saydırdı (tools/konum/pusula_dene.py, MOD=tam / ters).
            if (Mathf.Abs(tahmin) > 4f && ((!tuttu && Mathf.Abs(tahmin) > 8f) || (Mathf.Sign(adim) != Mathf.Sign(tahmin) && Mathf.Abs(adim) > 2f)))
            {
                if (tuttu) ReddedilenAdim++;
                Yaw = once + tahmin; TahminKullanildi = true;
                System.Array.Copy(bCur, bRef, bCur.Length); refYaw = Yaw; relYaw = 0;
            }
            else if (refle)
            {
                relYaw = s / PxDerece; Yaw = refYaw + relYaw; PusulaTuttu = true;
                if (Mathf.Abs(relYaw) > 25f || c < 0.45f) { System.Array.Copy(bCur, bRef, bCur.Length); refYaw = Yaw; relYaw = 0; }
            }
            else
            {
                if (tuttu) { Yaw += adim; PusulaTuttu = true; }
                System.Array.Copy(bCur, bRef, bCur.Length); refYaw = Yaw; relYaw = 0;
            }
            AdimDonus = Mathf.DeltaAngle(0f, Yaw - once); sonAdim = AdimDonus;
            System.Array.Copy(bCur, bPrev, bCur.Length);
        }

        static void Eslestir(float[] a, float[] b, float merkez, int aralik, out float s, out float c)
        {
            int c0 = Mathf.RoundToInt(merkez), n = 2 * aralik + 1; var k = new float[n + 2];
            float ea = 0f, eb = 0f; for (int i = 0; i < a.Length; i++) { ea += a[i] * a[i]; eb += b[i] * b[i]; }
            int en = 0; float ev = float.MinValue;
            for (int q = -1; q <= n; q++)
            {
                int m = (((c0 - aralik + q) % BW) + BW) % BW; float t = 0f;
                for (int y = 0; y < BH; y++)
                {
                    int o = y * BW;
                    for (int x = 0; x < BW; x++) { int xs = x + m; if (xs >= BW) xs -= BW; t += a[o + xs] * b[o + x]; }
                }
                k[q + 1] = t; if (q >= 0 && q < n && t > ev) { ev = t; en = q; }
            }
            float y0 = k[en], y1 = k[en + 1], y2 = k[en + 2], p = y0 - 2f * y1 + y2;
            s = c0 - aralik + en + (Mathf.Abs(p) > 1e-6f ? Mathf.Clamp(0.5f * (y0 - y2) / p, -0.5f, 0.5f) : 0f);
            c = ev / (Mathf.Sqrt(ea * eb) + 1e-6f);
        }

        // ------------------------------------------------------------------ zemin (ileri / yan)
        void Zemin(Color32[] d)
        {
            for (int i = 0; i < KN * KN; i++) kCur[i] = d[i].r;
            if (!kVar) { System.Array.Copy(kCur, kPrev, kCur.Length); kVar = true; return; }
            // robot sağa θ dönünce eski karedeki nokta (x, z) yeni karede (x cosθ - z sinθ, x sinθ + z cosθ)
            float ct = Mathf.Cos(AdimDonus * Mathf.Deg2Rad), st = Mathf.Sin(AdimDonus * Mathf.Deg2Rad);
            var dx = new System.Collections.Generic.List<float>(); var dz = new System.Collections.Generic.List<float>();
            for (int gy = Yama + Ara + 2; gy < KN - Yama - Ara - 2; gy += IzGrid)
                for (int gx = Yama + Ara + 2; gx < KN - Yama - Ara - 2; gx += IzGrid)
                {
                    if (kMaske[gy * KN + gx] < 0.5f) continue;
                    // doku: yamada yeterli kenar
                    float ort = 0f; for (int v = -Yama; v <= Yama; v++) for (int u = -Yama; u <= Yama; u++) ort += kPrev[(gy + v) * KN + gx + u];
                    ort /= (2 * Yama + 1) * (2 * Yama + 1);
                    float sap = 0f; for (int v = -Yama; v <= Yama; v++) for (int u = -Yama; u <= Yama; u++) sap += Mathf.Abs(kPrev[(gy + v) * KN + gx + u] - ort);
                    if (sap / ((2 * Yama + 1) * (2 * Yama + 1)) < 6f) continue;
                    // beklenen yer (yalnız dönüşle)
                    float x = ((gx + 0.5f) / KN * 2f - 1f) * KR, z = ((gy + 0.5f) / KN * 2f - 1f) * KR;
                    float xb = x * ct - z * st, zb = x * st + z * ct;
                    int bx = Mathf.RoundToInt((xb / KR + 1f) * 0.5f * KN - 0.5f), by = Mathf.RoundToInt((zb / KR + 1f) * 0.5f * KN - 0.5f);
                    if (bx - Ara - Yama < 0 || by - Ara - Yama < 0 || bx + Ara + Yama >= KN || by + Ara + Yama >= KN) continue;
                    float en = float.MaxValue, ikinci = float.MaxValue; int ex = 0, ey = 0;
                    for (int oy = -Ara; oy <= Ara; oy++)
                        for (int ox = -Ara; ox <= Ara; ox++)
                        {
                            float ortB = 0f;
                            for (int v = -Yama; v <= Yama; v++) for (int u = -Yama; u <= Yama; u++) ortB += kCur[(by + oy + v) * KN + bx + ox + u];
                            ortB /= (2 * Yama + 1) * (2 * Yama + 1);
                            float sad = 0f;
                            for (int v = -Yama; v <= Yama && sad < en; v++)
                                for (int u = -Yama; u <= Yama; u++)
                                    sad += Mathf.Abs((kPrev[(gy + v) * KN + gx + u] - ort) - (kCur[(by + oy + v) * KN + bx + ox + u] - ortB));
                            if (sad < en) { if (Mathf.Abs(ox - ex) + Mathf.Abs(oy - ey) > 2) ikinci = en; en = sad; ex = ox; ey = oy; }
                        }
                    if (ikinci < en * 1.15f) continue;                           // belirsiz (tekrarlı desen)
                    if (Mathf.Abs(ex) == Ara || Mathf.Abs(ey) == Ara) continue;   // arama sınırında
                    // ölçülen - beklenen (metre): yeni karede noktanın -sağ, -ileri kadar kayması
                    float mx = (bx + ex - bx) / KPx, mz = (by + ey - by) / KPx;
                    dx.Add(mx); dz.Add(mz);
                }
            System.Array.Copy(kCur, kPrev, kCur.Length);
            if (dx.Count < 8) return;
            float ox2 = Ortanca(dx), oz2 = Ortanca(dz); int ic = 0; float sx = 0f, sz = 0f;
            for (int i = 0; i < dx.Count; i++) if (Mathf.Abs(dx[i] - ox2) < 0.02f && Mathf.Abs(dz[i] - oz2) < 0.02f) { ic++; sx += dx[i]; sz += dz[i]; }
            ZeminNokta = ic;
            if (ic < 8 || ic < dx.Count / 2) return;
            // ortanca 1 cm'ye yuvarli; uyan yamalarin ortalamasi alt piksel (15 Hz'de adim ~2 cm, yuvarlama %10+ hata verirdi)
            AdimSag = -sx / ic; AdimIleri = -sz / ic; ZeminTuttu = true;
        }

        static float Ortanca(System.Collections.Generic.List<float> a)
        {
            var b = a.ToArray(); System.Array.Sort(b); return b[b.Length / 2];
        }
    }
}
