using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ArTarama
{
    /// <summary>Tek/cift parmak hareketlerini okur: bir parmak surukle, iki parmak yakinlastir + kaydir.</summary>
    public class HaritaDokunma
    {
        public RectTransform alan;
        public Vector2 tekDelta;       // piksel
        public float yakinlik = 1f;    // bu karedeki olcek carpani (1 = degisim yok)
        public Vector2 ciftDelta;      // iki parmak orta noktasi hareketi (piksel)
        public int Parmak;
        readonly Dictionary<int, bool> izinli = new Dictionary<int, bool>();
        float oncekiMesafe;

        public void Oku()
        {
            tekDelta = Vector2.zero; yakinlik = 1f; ciftDelta = Vector2.zero; Parmak = 0;
            var ts = Touchscreen.current;
            if (ts == null) return;
            var aktif = new List<UnityEngine.InputSystem.Controls.TouchControl>(2);
            foreach (var t in ts.touches)
            {
                var ph = t.phase.ReadValue();
                int id = t.touchId.ReadValue();
                if (ph == UnityEngine.InputSystem.TouchPhase.Began)
                    izinli[id] = alan == null || RectTransformUtility.RectangleContainsScreenPoint(alan, t.position.ReadValue());
                if (ph == UnityEngine.InputSystem.TouchPhase.None || ph == UnityEngine.InputSystem.TouchPhase.Ended || ph == UnityEngine.InputSystem.TouchPhase.Canceled) continue;
                if (izinli.TryGetValue(id, out bool ok) && ok) aktif.Add(t);
            }
            Parmak = aktif.Count;
            if (aktif.Count == 1)
            {
                tekDelta = aktif[0].delta.ReadValue(); oncekiMesafe = 0;
            }
            else if (aktif.Count >= 2)
            {
                Vector2 a = aktif[0].position.ReadValue(), b = aktif[1].position.ReadValue();
                Vector2 da = aktif[0].delta.ReadValue(), db = aktif[1].delta.ReadValue();
                float m = Vector2.Distance(a, b);
                if (oncekiMesafe > 1f) yakinlik = m / oncekiMesafe;
                oncekiMesafe = m;
                ciftDelta = (da + db) * 0.5f;
            }
            else oncekiMesafe = 0;
        }
    }

    /// <summary>3B harita: ayri kamera, yorunge kontrolu, nokta bulutu + duzlemler + yol izi + kamera piramidi.</summary>
    public class Harita3B : MonoBehaviour
    {
        public Camera kam;
        public Camera arKamera;
        public NoktaBulutu bulut;
        public PlanHaritasi plan;
        public RectTransform dokunmaAlani;
        public const int OzelKatman = 9;

        public bool Acik { get; private set; }
        public bool YukseklikRengi = true;
        public bool DuzlemGoster = true;
        public bool NoktaGoster;   // ham nokta bulutu 3B haritada varsayilan kapali: oda modeli on planda

        float yaw = 35f, pitch = 55f, mesafe = 8f;
        Vector3 hedef;
        LineRenderer iz, piramit;
        readonly List<GameObject> isaretKutular = new List<GameObject>();
        Material cizgiMat, isaretMat;
        readonly HaritaDokunma dokunma = new HaritaDokunma();
        int izBitis;

        void Awake()
        {
            cizgiMat = Resources.Load<Material>("mat_cizgi");
            iz = Cizgi("YolIzi", new Color(0.93f, 0.92f, 0.9f, 0.95f), 0.03f);
            piramit = Cizgi("Piramit", UiKur.Vurgu, 0.018f);
            piramit.loop = false;
            kam.enabled = false;
            kam.cullingMask = Maske(false);
            kam.clearFlags = CameraClearFlags.SolidColor;
            kam.backgroundColor = new Color(0.047f, 0.055f, 0.067f, 1f);
            kam.nearClipPlane = 0.05f; kam.farClipPlane = 300f; kam.depth = 20; kam.allowHDR = false; kam.allowMSAA = false;
            dokunma.alan = dokunmaAlani;
        }

        LineRenderer Cizgi(string ad, Color c, float genislik)
        {
            var go = new GameObject(ad); go.layer = OzelKatman; go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = cizgiMat; lr.useWorldSpace = true; lr.startWidth = lr.endWidth = genislik;
            lr.startColor = lr.endColor = c; lr.numCapVertices = 2; lr.numCornerVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            lr.positionCount = 0; lr.alignment = LineAlignment.View;
            return lr;
        }

        public void Ac()
        {
            Acik = true;
            kam.enabled = true;
            Merkezle();
            Shader.SetGlobalFloat("_NoktaMod", YukseklikRengi ? 1f : 0f);
            Shader.SetGlobalFloat("_NoktaBoyut", 3.5f);
            izBitis = 0;
        }

        public void Kapat()
        {
            Acik = false;
            kam.enabled = false;
            Shader.SetGlobalFloat("_NoktaMod", 0f);
            Shader.SetGlobalFloat("_NoktaBoyut", 7f);
        }

        /// <summary>3B gorunum katmanlari: nokta/duzlem (8), cizgiler (9) ve katı oda modeli (12). 2B sekmesinde hicbiri cizilmez.</summary>
        public static int Maske(bool ikiB) { return ikiB ? 0 : ((1 << NoktaBulutu.HaritaKatmani) | (1 << OzelKatman) | (1 << OdaYoneticisi.KatmanHarita)); }
        public void MaskeUygula(bool ikiB) { kam.cullingMask = Maske(ikiB); }

        public void ModAyarla() { Shader.SetGlobalFloat("_NoktaMod", YukseklikRengi ? 1f : 0f); }

        public void Merkezle()
        {
            if (bulut != null && bulut.SinirVar)
            {
                Vector3 mn = bulut.Min, mx = bulut.Maks;
                hedef = (mn + mx) * 0.5f;
                float yaricap = Mathf.Max(0.5f * (mx - mn).magnitude, 2f);
                mesafe = Mathf.Clamp(yaricap * 1.5f, 3f, 60f);
            }
            else { hedef = arKamera.transform.position; mesafe = 6f; }
        }

        void LateUpdate()
        {
            if (!Acik) return;
            dokunma.alan = dokunmaAlani;
            dokunma.Oku();
            if (dokunma.Parmak == 1)
            {
                yaw += dokunma.tekDelta.x * 0.22f;
                pitch = Mathf.Clamp(pitch - dokunma.tekDelta.y * 0.22f, 4f, 89f);
            }
            else if (dokunma.Parmak >= 2)
            {
                mesafe = Mathf.Clamp(mesafe / Mathf.Clamp(dokunma.yakinlik, 0.8f, 1.25f), 0.8f, 120f);
                float s = mesafe * 0.0011f;
                hedef -= kam.transform.right * dokunma.ciftDelta.x * s - kam.transform.up * dokunma.ciftDelta.y * s;
            }
            var rot = Quaternion.Euler(pitch, yaw, 0);
            kam.transform.position = hedef - rot * Vector3.forward * mesafe;
            kam.transform.rotation = rot;

            // yol izi
            if (plan != null && plan.Iz.Count != izBitis)
            {
                var l = plan.Iz; iz.positionCount = l.Count;
                for (int i = izBitis; i < l.Count; i++) iz.SetPosition(i, l[i] + Vector3.up * 0.02f);
                if (izBitis == 0) for (int i = 0; i < l.Count; i++) iz.SetPosition(i, l[i] + Vector3.up * 0.02f);
                izBitis = l.Count;
            }
            // kamera piramidi
            var t = arKamera.transform; float d = 0.28f;
            Vector3 a = t.position;
            Vector3 c1 = a + t.rotation * new Vector3(-0.5f * d * 0.56f, 0.5f * d, d), c2 = a + t.rotation * new Vector3(0.5f * d * 0.56f, 0.5f * d, d);
            Vector3 c3 = a + t.rotation * new Vector3(0.5f * d * 0.56f, -0.5f * d, d), c4 = a + t.rotation * new Vector3(-0.5f * d * 0.56f, -0.5f * d, d);
            piramit.positionCount = 11;
            Vector3[] p = { a, c1, c2, c3, c4, c1, a, c2, c3, a, c4 };
            piramit.SetPositions(p);

            // isaret kutulari
            int n = 0;
            if (plan != null)
                foreach (var b in plan.Isaretler.Values)
                {
                    if (!b.konumVar) continue;
                    if (n >= isaretKutular.Count)
                    {
                        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        Destroy(g.GetComponent<Collider>());
                        g.layer = OzelKatman; g.transform.SetParent(transform, false); g.transform.localScale = Vector3.one * 0.14f;
                        var mr = g.GetComponent<MeshRenderer>(); mr.sharedMaterial = cizgiMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        var mpb = new MaterialPropertyBlock(); mpb.SetColor("_Color", UiKur.Uyari); mr.SetPropertyBlock(mpb);
                        isaretKutular.Add(g);
                    }
                    isaretKutular[n].SetActive(true); isaretKutular[n].transform.position = b.konum; n++;
                }
            for (int i = n; i < isaretKutular.Count; i++) isaretKutular[i].SetActive(false);
        }

        public void Temizle()
        {
            iz.positionCount = 0; izBitis = 0;
            foreach (var g in isaretKutular) Destroy(g);
            isaretKutular.Clear();
        }
    }
}
