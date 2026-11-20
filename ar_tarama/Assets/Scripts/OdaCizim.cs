using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArTarama
{
    /// <summary>
    /// AR goruntusunun ustune RoomPlan tarzi canli oda cizgileri. Kesin duvar: net beyaz zemin/tavan cizgisi, dikey kose cizgileri,
    /// kose isaretleri, cok hafif ton; kesinlesince cizilerek acilir. Aday duvar: ince kesik cizgi. Kesin kutu: net beyaz kenar +
    /// cok hafif dolgu, yumusak belirme. Aday kutu: yalniz soluk kose ayraclari. Cizgiler kamera yonlu seritlerdir (ArTarama/OdaCizgi).
    /// </summary>
    public class OdaArCizim
    {
        GameObject goCizgi, goDolgu; Mesh mCizgi, mDolgu; MeshRenderer rc, rd; Material mc, md;
        readonly List<Vector3> cv = new List<Vector3>(), co = new List<Vector3>(), dv = new List<Vector3>();
        readonly List<Vector2> cu = new List<Vector2>();
        readonly List<Color> cc = new List<Color>(), dc = new List<Color>();
        readonly List<int> ci = new List<int>(), di = new List<int>();
        readonly List<Vector2> koseler = new List<Vector2>();
        Vector3 camPos, camFwd;

        public OdaArCizim(Material cizgi, Material dolgu)
        {
            mc = cizgi != null ? new Material(cizgi) : new Material(Shader.Find("ArTarama/OdaCizgi"));
            md = dolgu != null ? new Material(dolgu) : new Material(Shader.Find("ArTarama/OdaDolgu"));
            mCizgi = new Mesh { name = "OdaCizgi" }; mCizgi.MarkDynamic();
            mDolgu = new Mesh { name = "OdaDolgu" }; mDolgu.MarkDynamic();
            goCizgi = Nesne("OdaCizgiler", mCizgi, mc, out rc);
            goDolgu = Nesne("OdaDolgu", mDolgu, md, out rd);
        }

        static GameObject Nesne(string ad, Mesh m, Material mat, out MeshRenderer r)
        {
            var go = new GameObject(ad); go.layer = OdaYoneticisi.KatmanAr;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.allowOcclusionWhenDynamic = false;
            return go;
        }

        public void Yok() { Object.Destroy(goCizgi); Object.Destroy(goDolgu); Object.Destroy(mCizgi); Object.Destroy(mDolgu); }

        public void Temizle() { mCizgi.Clear(); mDolgu.Clear(); }

        static float Yum(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        public void Guncelle(OdaYoneticisi y, Camera kam, bool goster)
        {
            rc.enabled = rd.enabled = goster;
            if (!goster) return;
            cv.Clear(); co.Clear(); cu.Clear(); cc.Clear(); ci.Clear(); dv.Clear(); dc.Clear(); di.Clear(); koseler.Clear();
            camPos = kam.transform.position; camFwd = kam.transform.forward;
            float simdi = Time.unscaledTime;
            var g = y.Goruntu;
            float zy = g.GZeminY, yuk = Mathf.Max(1.5f, g.GTavanY - g.GZeminY);
            mc.SetFloat("_Genislik", Mathf.Max(2.2f, 3.2f * Screen.height / 2400f));
            mc.SetFloat("_Parilti", Mathf.Max(3.5f, 6f * Screen.height / 2400f));
            float tavanA = g.TavanTahmin ? 0.5f : 0.8f;

            // taranan zemin: hafif turkuaz boya (taranmamis yer bos kalir; kullanici neresinin eksik oldugunu gorur)
            {
                var zb = g.ZeminBoya; float hh = OdaGoruntu.BoyaHucre * 0.5f - 0.01f, yb = zy + 0.008f;
                var renk = new Color(0.26f, 0.85f, 0.75f, 0.12f);
                for (int i = 0; i + 1 < zb.Length; i += 2)
                {
                    float x = zb[i], z = zb[i + 1];
                    if ((x - camPos.x) * (x - camPos.x) + (z - camPos.z) * (z - camPos.z) > 36f) continue;
                    Dolgu(new Vector3(x - hh, yb, z - hh), new Vector3(x + hh, yb, z - hh), new Vector3(x + hh, yb, z + hh), new Vector3(x - hh, yb, z + hh), renk);
                }
            }

            foreach (var d in g.Duvarlar)
            {
                var a0 = new Vector3(d.ax, zy, d.az); var b0 = new Vector3(d.bx, zy, d.bz);
                if (!d.kesin)
                {
                    float al = 0.22f + 0.13f * d.ilerleme;     // ~%35
                    Kesik(a0, b0, al, 0.7f);
                    Kesik(a0 + Vector3.up * yuk, b0 + Vector3.up * yuk, al * 0.7f, 0.6f);
                    continue;
                }
                float ac = d.Acilis(simdi);
                float p1 = Yum(ac / 0.5f), p2 = Yum((ac - 0.3f) / 0.4f), p3 = Yum((ac - 0.6f) / 0.4f);
                var m0 = (a0 + b0) * 0.5f;
                Cizgi(Vector3.Lerp(m0, a0, p1), Vector3.Lerp(m0, b0, p1), 0.95f, 1f);             // zemin
                float h = yuk * p2;
                var a1 = a0 + Vector3.up * h; var b1 = b0 + Vector3.up * h; var m1 = (a1 + b1) * 0.5f;
                if (p3 > 0f) Cizgi(Vector3.Lerp(m1, a1, p3), Vector3.Lerp(m1, b1, p3), tavanA, 1f);   // tavan kenari
                if (d.aKose && Yeni(d.ax, d.az)) Cizgi(a0, a1, 0.8f * p2, 1f);                        // dikey kose cizgileri
                if (d.bKose && Yeni(d.bx, d.bz)) Cizgi(b0, b1, 0.8f * p2, 1f);
                // kose isaretleri: zeminde iki yone kisa kalin ayrac (iki duvar birlesince L olur)
                Vector3 e = (b0 - a0); float L = e.magnitude; if (L > 0.05f)
                {
                    e /= L; float kol = Mathf.Min(0.2f, L * 0.3f) * p1;
                    if (d.aKose) Cizgi(a0, a0 + e * kol, 1f, 2.1f);
                    if (d.bKose) Cizgi(b0, b0 - e * kol, 1f, 2.1f);
                }
                Dolgu(a0, b0, b1, a1, new Color(1f, 1f, 1f, 0.035f * p2));
            }

            foreach (var b in g.Kutular)
            {
                double r = b.aci * Mathf.Deg2Rad;
                var u = new Vector3((float)System.Math.Cos(r), 0, (float)System.Math.Sin(r)); var w = new Vector3(-u.z, 0, u.x);
                float s = b.Olcek(simdi), v = b.Gorunurluk(simdi);
                float tab = b.cy - b.sy * 0.5f;
                float hx = b.sx * 0.5f * s, hz = b.sz * 0.5f * s, hy = b.sy * s;
                var taban = new Vector3(b.cx, tab, b.cz);
                var p = new Vector3[8];
                for (int i = 0; i < 4; i++)
                {
                    float sx = (i == 0 || i == 3) ? -1 : 1, sz = i < 2 ? -1 : 1;
                    p[i] = taban + u * (sx * hx) + w * (sz * hz);
                    p[i + 4] = p[i] + Vector3.up * hy;
                }
                if (b.kesin)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        int j = (i + 1) % 4;
                        Cizgi(p[i], p[j], 0.9f * v, 0.9f); Cizgi(p[i + 4], p[j + 4], 0.9f * v, 0.9f); Cizgi(p[i], p[i + 4], 0.9f * v, 0.9f);
                        Dolgu(p[i], p[j], p[j + 4], p[i + 4], new Color(1f, 1f, 1f, 0.06f * v));
                    }
                    Dolgu(p[4], p[5], p[6], p[7], new Color(1f, 1f, 1f, 0.09f * v));
                }
                else
                {
                    float kol = Mathf.Min(0.10f, 0.3f * Mathf.Min(b.sx, Mathf.Min(b.sz, b.sy)));
                    float al = 0.30f + 0.15f * b.ilerleme;
                    for (int i = 0; i < 8; i++)
                    {
                        int n1 = (i & 3) == 3 ? i - 3 : i + 1, n2 = (i & 3) == 0 ? i + 3 : i - 1, n3 = i < 4 ? i + 4 : i - 4;
                        foreach (int n in new[] { n1, n2, n3 })
                        {
                            Vector3 dir = (p[n] - p[i]); float l = dir.magnitude; if (l < 1e-4f) continue;
                            Cizgi(p[i], p[i] + dir / l * Mathf.Min(kol, l * 0.5f), al, 0.8f);
                        }
                    }
                }
            }

            mCizgi.Clear();
            if (cv.Count > 0)
            {
                mCizgi.SetVertices(cv); mCizgi.SetUVs(0, cu); mCizgi.SetUVs(1, co); mCizgi.SetColors(cc); mCizgi.SetTriangles(ci, 0, false);
                mCizgi.bounds = new Bounds(camPos, Vector3.one * 200f);
            }
            mDolgu.Clear();
            if (dv.Count > 0)
            {
                mDolgu.SetVertices(dv); mDolgu.SetColors(dc); mDolgu.SetTriangles(di, 0, false);
                mDolgu.bounds = new Bounds(camPos, Vector3.one * 200f);
            }
        }

        bool Yeni(float x, float z)
        {
            foreach (var k in koseler) if ((k.x - x) * (k.x - x) + (k.y - z) * (k.y - z) < 0.06f * 0.06f) return false;
            koseler.Add(new Vector2(x, z)); return true;
        }

        /// <summary>Kesik cizgi: 9 cm cizgi + 9 cm bosluk.</summary>
        void Kesik(Vector3 a, Vector3 b, float alfa, float gen)
        {
            Vector3 e = b - a; float L = e.magnitude; if (L < 0.05f) return; e /= L;
            const float cz = 0.09f, bs = 0.09f;
            for (float t = 0; t < L; t += cz + bs) Cizgi(a + e * t, a + e * Mathf.Min(L, t + cz), alfa, gen);
        }

        void Cizgi(Vector3 a, Vector3 b, float alfa, float gen)
        {
            if (alfa <= 0.003f) return;
            const float yakin = 0.12f;
            float da = Vector3.Dot(a - camPos, camFwd) - yakin, db = Vector3.Dot(b - camPos, camFwd) - yakin;
            if (da < 0 && db < 0) return;
            if (da < 0) a = Vector3.Lerp(a, b, -da / (db - da)); else if (db < 0) b = Vector3.Lerp(b, a, -db / (da - db));
            var c = new Color(1f, 1f, 1f, alfa);
            int n = cv.Count;
            cv.Add(a); co.Add(b); cu.Add(new Vector2(gen, -1)); cc.Add(c);
            cv.Add(a); co.Add(b); cu.Add(new Vector2(gen, 1)); cc.Add(c);
            cv.Add(b); co.Add(a); cu.Add(new Vector2(gen, 1)); cc.Add(c);
            cv.Add(b); co.Add(a); cu.Add(new Vector2(gen, -1)); cc.Add(c);
            ci.Add(n); ci.Add(n + 1); ci.Add(n + 2); ci.Add(n); ci.Add(n + 2); ci.Add(n + 3);
        }

        void Dolgu(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color renk)
        {
            if (renk.a <= 0.002f) return;
            int n = dv.Count;
            dv.Add(a); dv.Add(b); dv.Add(c); dv.Add(d); dc.Add(renk); dc.Add(renk); dc.Add(renk); dc.Add(renk);
            di.Add(n); di.Add(n + 1); di.Add(n + 2); di.Add(n); di.Add(n + 2); di.Add(n + 3);
        }
    }

    /// <summary>
    /// Duz golgeli katı oda modeli (alt mini panel ve inceleme ekrani): koyu zemin, kalin beyaz duvarlar (ust kenar parlak),
    /// acik renk kutular ve istege bagli kullanici noktasi + yon konisi. Yalniz KESIN geometri cizilir.
    /// </summary>
    public class OdaKatiModel
    {
        GameObject go; Mesh mesh; MeshRenderer mr;
        readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        readonly List<Color32> c = new List<Color32>();
        readonly List<int> t = new List<int>();
        readonly Dictionary<int, float> kesme = new Dictionary<int, float>();
        public Vector3 Merkez { get; private set; }
        public float Yaricap { get; private set; } = 2f;
        public bool Dolu { get; private set; }
        public Bounds Sinir { get; private set; }
        public const float DuvarKalinlik = 0.10f;

        static readonly Color32 ZeminRenk = new Color32(46, 52, 60, 255), DuvarYan = new Color32(214, 218, 225, 255), DuvarUst = new Color32(250, 251, 253, 255),
            KutuUst = new Color32(255, 255, 255, 255), KutuYan = new Color32(186, 204, 214, 255);
        static readonly Color32 Vurgu = new Color32(0x43, 0xD9, 0xBE, 255), Koni = new Color32(0x8F, 0xEC, 0xDD, 255), Hale = new Color32(255, 255, 255, 255);

        public OdaKatiModel(int katman, Material mat, string ad)
        {
            mesh = new Mesh { name = ad }; mesh.MarkDynamic();
            go = new GameObject(ad); go.layer = katman;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat != null ? mat : new Material(Shader.Find("ArTarama/OdaKati"));
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.allowOcclusionWhenDynamic = false;
        }

        public void Yok() { Object.Destroy(go); Object.Destroy(mesh); }

        /// <summary>Modeli yeniden uretir. kam: duvar kesme (yakin duvarlari alcaltma) icin bakis kamerasi.</summary>
        public void Guncelle(OdaGoruntu y, Camera kam, bool goster, bool kullanici, Vector3 kPos, float kYaw, bool kesmeli)
        {
            mr.enabled = goster;
            if (!goster) return;
            v.Clear(); n.Clear(); c.Clear(); t.Clear();
            float zy = y.GZeminY, yuk = Mathf.Max(1.5f, y.GTavanY - y.GZeminY);
            float simdi = Time.unscaledTime, dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);

            bool bos = true; Bounds bnd = new Bounds();
            System.Action<Vector3> kap = p => { if (bos) { bnd = new Bounds(p, Vector3.zero); bos = false; } else bnd.Encapsulate(p); };
            var zp = y.ZeminPoligon;
            for (int i = 0; i + 1 < zp.Length; i += 2) kap(new Vector3(zp[i], zy, zp[i + 1]));
            foreach (var d in y.Duvarlar) if (d.kesin) { kap(new Vector3(d.ax, zy, d.az)); kap(new Vector3(d.bx, zy + yuk, d.bz)); }
            foreach (var b in y.Kutular) if (b.kesin) kap(new Vector3(b.cx, b.cy, b.cz));
            if (kullanici) kap(kPos);
            Dolu = !bos; Sinir = bnd;
            if (!bos) { Merkez = new Vector3(bnd.center.x, zy + yuk * 0.25f, bnd.center.z); Yaricap = Mathf.Max(1.6f, 0.5f * new Vector2(bnd.size.x, bnd.size.z).magnitude + 0.2f); }
            float olcek = Mathf.Clamp(Yaricap * 0.06f, 0.07f, 0.28f);

            // zemin (basit coklu gen: kulak kesme)
            int zn = zp.Length / 2;
            if (zn >= 3)
            {
                var pl = new List<double[]>(); for (int i = 0; i < zn; i++) pl.Add(new double[] { zp[i * 2], zp[i * 2 + 1] });
                var tri = OdaGeo.Ucgenle(pl);
                for (int i = 0; i + 2 < tri.Count; i += 3)
                    Uc(new Vector3(zp[tri[i] * 2], zy, zp[tri[i] * 2 + 1]), new Vector3(zp[tri[i + 2] * 2], zy, zp[tri[i + 2] * 2 + 1]), new Vector3(zp[tri[i + 1] * 2], zy, zp[tri[i + 1] * 2 + 1]), Vector3.up, ZeminRenk);
            }

            // duvarlar: ic yuz cizgide, kalinlik disa dogru
            Vector3 bf = kam != null ? kam.transform.forward : Vector3.forward; bf.y = 0f; if (bf.sqrMagnitude > 1e-4f) bf.Normalize();
            foreach (var d in y.Duvarlar)
            {
                if (!d.kesin) continue;
                float ac = d.Acilis(simdi);
                Vector3 a = new Vector3(d.ax, zy, d.az), b = new Vector3(d.bx, zy, d.bz);
                Vector3 e = b - a; float len = e.magnitude; if (len < 0.05f) continue; e /= len;
                Vector3 nn = new Vector3(d.nx, 0, d.nz);
                float hedef = 1f;
                if (kesmeli && kam != null && Vector3.Dot(nn, bf) > 0.30f) hedef = 0.14f;   // kameraya yakin duvar alcalir (maket kesiti)
                kesme.TryGetValue(d.id, out float kv); if (kv <= 0f) kv = 1f;
                kv += (hedef - kv) * (1f - Mathf.Exp(-6f * dt)); kesme[d.id] = kv;
                Vector3 orta = (a + b) * 0.5f - nn * (DuvarKalinlik * 0.5f);
                Kutu(orta, e, nn, len * 0.5f + DuvarKalinlik * 0.5f, DuvarKalinlik * 0.5f, yuk * kv * Mathf.Max(ac, 0.02f), DuvarUst, DuvarYan);
            }

            // esyalar
            foreach (var b in y.Kutular)
            {
                if (!b.kesin) continue;
                float s = b.Olcek(simdi);
                double r = b.aci * Mathf.Deg2Rad;
                var u = new Vector3((float)System.Math.Cos(r), 0, (float)System.Math.Sin(r)); var w = new Vector3(-u.z, 0, u.x);
                Kutu(new Vector3(b.cx, b.cy - b.sy * 0.5f, b.cz), u, w, b.sx * 0.5f * s, b.sz * 0.5f * s, b.sy * s, KutuUst, KutuYan);
            }

            // kullanici
            if (kullanici)
            {
                float yy = zy + 0.012f; double r = kYaw * Mathf.Deg2Rad;
                var dir = new Vector3((float)System.Math.Sin(r), 0, (float)System.Math.Cos(r));
                var p = new Vector3(kPos.x, yy, kPos.z);
                Disk(p + Vector3.down * 0.004f, olcek * 1.55f, Hale);
                Disk(p, olcek, Vurgu);
                var sol = Quaternion.Euler(0, -27f, 0) * dir; var sag = Quaternion.Euler(0, 27f, 0) * dir;
                float L = olcek * 6f; var q = p + Vector3.up * 0.006f;
                Uc(q, q + sag * L, q + sol * L, Vector3.up, Koni);
            }

            mesh.Clear();
            if (v.Count > 0)
            {
                mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetColors(c); mesh.SetTriangles(t, 0, false);
                mesh.bounds = new Bounds(Merkez, Vector3.one * 400f);
            }
        }

        void Uc(Vector3 a, Vector3 b, Vector3 d, Vector3 nn, Color32 col)
        {
            int k = v.Count;
            v.Add(a); v.Add(b); v.Add(d); n.Add(nn); n.Add(nn); n.Add(nn); c.Add(col); c.Add(col); c.Add(col);
            t.Add(k); t.Add(k + 1); t.Add(k + 2);
        }

        void Disk(Vector3 p, float r, Color32 col)
        {
            const int N = 20;
            for (int i = 0; i < N; i++)
            {
                float a0 = i * 2f * Mathf.PI / N, a1 = (i + 1) * 2f * Mathf.PI / N;
                Uc(p, p + new Vector3(Mathf.Cos(a1) * r, 0, Mathf.Sin(a1) * r), p + new Vector3(Mathf.Cos(a0) * r, 0, Mathf.Sin(a0) * r), Vector3.up, col);
            }
        }

        void Yuz(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Vector3 nn, Color32 col)
        {
            int k = v.Count;
            v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
            for (int i = 0; i < 4; i++) { n.Add(nn); c.Add(col); }
            t.Add(k); t.Add(k + 1); t.Add(k + 2); t.Add(k); t.Add(k + 2); t.Add(k + 3);
        }

        /// <summary>taban merkezi, yerel x yonu u, yerel z yonu w (XZ), yari boyutlar ve yukseklik; yukari kapagi ust renk.</summary>
        void Kutu(Vector3 taban, Vector3 u, Vector3 w, float hx, float hz, float hy, Color32 ust, Color32 yan)
        {
            if (hy < 0.002f) return;
            Vector3 up = Vector3.up * hy;
            Vector3 p00 = taban - u * hx - w * hz, p10 = taban + u * hx - w * hz, p11 = taban + u * hx + w * hz, p01 = taban - u * hx + w * hz;
            Yuz(p00 + up, p10 + up, p11 + up, p01 + up, Vector3.up, ust);
            Yuz(p00, p10, p10 + up, p00 + up, -w, yan);
            Yuz(p10, p11, p11 + up, p10 + up, u, yan);
            Yuz(p11, p01, p01 + up, p11 + up, w, yan);
            Yuz(p01, p00, p00 + up, p01 + up, -u, yan);
        }
    }
}
