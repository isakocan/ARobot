using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArTarama
{
    /// <summary>
    /// AR goruntusunun ustune RoomPlan tarzi canli oda cizgileri: duvar tabani/tavan kenari/koseler ince beyaz parlayan cizgi,
    /// duvarlar cok hafif beyaz, esyalar yari saydam kutu. Cizgiler kamera yonlu seritlerdir (ArTarama/OdaCizgi, ekran uzayinda sabit kalinlik).
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

        public void Guncelle(OdaYoneticisi y, Camera kam, bool goster)
        {
            rc.enabled = rd.enabled = goster;
            if (!goster) return;
            cv.Clear(); co.Clear(); cu.Clear(); cc.Clear(); ci.Clear(); dv.Clear(); dc.Clear(); di.Clear(); koseler.Clear();
            camPos = kam.transform.position; camFwd = kam.transform.forward;
            float simdi = Time.unscaledTime;
            float zy = y.GZeminY, yuk = Mathf.Max(1.5f, y.GTavanY - y.GZeminY);
            mc.SetFloat("_Genislik", Mathf.Max(2.2f, 3f * Screen.height / 2400f));
            mc.SetFloat("_Parilti", Mathf.Max(7f, 11f * Screen.height / 2400f));

            foreach (var d in y.Duvarlar)
            {
                float ac = d.Acilis(simdi), h = yuk * ac, fade = Mathf.Clamp01(ac * 2.5f);
                var a0 = new Vector3(d.ax, zy, d.az); var b0 = new Vector3(d.bx, zy, d.bz);
                var a1 = a0 + Vector3.up * h; var b1 = b0 + Vector3.up * h;
                Cizgi(a0, b0, fade);                   // zemin kenari
                Cizgi(a1, b1, 0.85f * fade);           // tavan kenari
                if (Yeni(d.ax, d.az)) Cizgi(a0, a1, fade);   // dikey kose cizgileri (ortak koseler bir kez)
                if (Yeni(d.bx, d.bz)) Cizgi(b0, b1, fade);
                Dolgu(a0, b0, b1, a1, new Color(1f, 1f, 1f, 0.06f * fade));
            }

            foreach (var b in y.Kutular)
            {
                float s = b.Olcek(simdi), v = b.Gorunurluk(simdi);
                float tab = b.cy - b.sy * 0.5f;
                double r = b.aci * Mathf.Deg2Rad;
                var u = new Vector3((float)System.Math.Cos(r), 0, (float)System.Math.Sin(r)); var w = new Vector3(-u.z, 0, u.x);
                float hx = b.sx * 0.5f * s, hz = b.sz * 0.5f * s, hy = b.sy * s;
                var taban = new Vector3(b.cx, tab, b.cz);
                var p = new Vector3[8];
                for (int i = 0; i < 4; i++)
                {
                    float sx = (i == 0 || i == 3) ? -1 : 1, sz = i < 2 ? -1 : 1;
                    p[i] = taban + u * (sx * hx) + w * (sz * hz);
                    p[i + 4] = p[i] + Vector3.up * hy;
                }
                // halka sirasi: 0(-,-) 1(+,-) 2(+,+) 3(-,+)
                for (int i = 0; i < 4; i++)
                {
                    int j = (i + 1) % 4;
                    Cizgi(p[i], p[j], 0.9f * v); Cizgi(p[i + 4], p[j + 4], 0.9f * v); Cizgi(p[i], p[i + 4], 0.9f * v);
                    Dolgu(p[i], p[j], p[j + 4], p[i + 4], new Color(1f, 1f, 1f, 0.11f * v));
                }
                Dolgu(p[4], p[5], p[6], p[7], new Color(1f, 1f, 1f, 0.14f * v));
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
            foreach (var k in koseler) if ((k.x - x) * (k.x - x) + (k.y - z) * (k.y - z) < 0.05f * 0.05f) return false;
            koseler.Add(new Vector2(x, z)); return true;
        }

        void Cizgi(Vector3 a, Vector3 b, float alfa)
        {
            if (alfa <= 0.003f) return;
            const float yakin = 0.12f;
            float da = Vector3.Dot(a - camPos, camFwd) - yakin, db = Vector3.Dot(b - camPos, camFwd) - yakin;
            if (da < 0 && db < 0) return;
            if (da < 0) a = Vector3.Lerp(a, b, -da / (db - da)); else if (db < 0) b = Vector3.Lerp(b, a, -db / (da - db));
            var c = new Color(1f, 1f, 1f, alfa);
            int n = cv.Count;
            cv.Add(a); co.Add(b); cu.Add(new Vector2(0, -1)); cc.Add(c);
            cv.Add(a); co.Add(b); cu.Add(new Vector2(0, 1)); cc.Add(c);
            cv.Add(b); co.Add(a); cu.Add(new Vector2(1, 1)); cc.Add(c);     // b ucunda yan isareti ters (ustten dogru serit)
            cv.Add(b); co.Add(a); cu.Add(new Vector2(1, -1)); cc.Add(c);
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
    /// Duz golgeli katı oda modeli (alt mini panel ve 3B harita): zemin poligonu, ince duvar panelleri (ust kenari koyu),
    /// esya kutulari ve istege bagli kullanici noktasi + yon konisi. Her ornek kendi katmaninda cizilir.
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

        static readonly Color32 ZeminRenk = new Color32(228, 232, 238, 255), DuvarYan = new Color32(196, 202, 212, 255), DuvarUst = new Color32(64, 72, 86, 255),
            KutuUst = new Color32(250, 252, 255, 255), KutuYan = new Color32(206, 220, 244, 255);
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
        public void Guncelle(OdaYoneticisi y, Camera kam, bool goster, bool kullanici, Vector3 kPos, float kYaw, bool kesmeli)
        {
            mr.enabled = goster;
            if (!goster) return;
            v.Clear(); n.Clear(); c.Clear(); t.Clear();
            float zy = y.GZeminY, yuk = Mathf.Max(1.5f, y.GTavanY - y.GZeminY);
            float simdi = Time.unscaledTime, dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);

            // sinirlar
            bool bos = true; Bounds bnd = new Bounds();
            System.Action<Vector3> kap = p => { if (bos) { bnd = new Bounds(p, Vector3.zero); bos = false; } else bnd.Encapsulate(p); };
            var zp = y.ZeminPoligon;
            for (int i = 0; i + 1 < zp.Length; i += 2) kap(new Vector3(zp[i], zy, zp[i + 1]));
            foreach (var d in y.Duvarlar) { kap(new Vector3(d.ax, zy, d.az)); kap(new Vector3(d.bx, zy + yuk, d.bz)); }
            foreach (var b in y.Kutular) kap(new Vector3(b.cx, b.cy, b.cz));
            if (kullanici) kap(kPos);
            Dolu = !bos;
            if (!bos) { Merkez = new Vector3(bnd.center.x, zy + yuk * 0.25f, bnd.center.z); Yaricap = Mathf.Max(1.6f, 0.5f * new Vector2(bnd.size.x, bnd.size.z).magnitude + 0.2f); }
            float olcek = Mathf.Clamp(Yaricap * 0.06f, 0.07f, 0.28f);

            // zemin (bukey poligon, merkezden yelpaze)
            int zn = zp.Length / 2;
            if (zn >= 3)
            {
                float mx = 0, mz = 0; for (int i = 0; i < zn; i++) { mx += zp[i * 2]; mz += zp[i * 2 + 1]; }
                mx /= zn; mz /= zn;
                var m0 = new Vector3(mx, zy, mz);
                for (int i = 0; i < zn; i++)
                {
                    int j = (i + 1) % zn;
                    Uc(m0, new Vector3(zp[j * 2], zy, zp[j * 2 + 1]), new Vector3(zp[i * 2], zy, zp[i * 2 + 1]), Vector3.up, ZeminRenk);
                }
            }

            // duvarlar
            Vector3 bf = kam != null ? kam.transform.forward : Vector3.forward; bf.y = 0f; if (bf.sqrMagnitude > 1e-4f) bf.Normalize();
            foreach (var d in y.Duvarlar)
            {
                float ac = d.Acilis(simdi);
                Vector3 a = new Vector3(d.ax, zy, d.az), b = new Vector3(d.bx, zy, d.bz);
                Vector3 e = b - a; float len = e.magnitude; if (len < 0.05f) continue; e /= len;
                Vector3 nn = new Vector3(d.nx, 0, d.nz);
                float hedef = 1f;
                if (kesmeli && kam != null && Vector3.Dot(nn, bf) > 0.30f) hedef = 0.16f;   // kameraya yakin duvar alcalir (maket kesiti)
                kesme.TryGetValue(d.id, out float kv); if (kv <= 0f) kv = 1f;
                kv += (hedef - kv) * (1f - Mathf.Exp(-6f * dt)); kesme[d.id] = kv;
                Kutu((a + b) * 0.5f, e, nn, len * 0.5f + 0.03f, 0.03f, yuk * kv * ac, DuvarUst, DuvarYan);
            }

            // esyalar
            foreach (var b in y.Kutular)
            {
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
