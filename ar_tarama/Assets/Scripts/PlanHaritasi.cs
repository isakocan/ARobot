using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ArTarama
{
    public class IsaretBilgi
    {
        public int id; public Vector3 konum; public bool konumVar; public int sayi; public float sonGorulme;
        // poz (4 koseden, 18 cm): agirlikli ortalama merkez ve yatay bakis yonu (isaretten odaya, oturum koordinati)
        public bool pozVar; public int pozSay; public Vector3 merkez; public float aci;   // aci: atan2(nx, nz), radyan
        public double sx, sy, sz, snx, snz, sw;
    }

    /// <summary>Tarama oturumunun hafif verisi: kamera izi, goruntulenen ArUco isaretleri ve yerlestirilen kristaller. (v4: 2B harita dokusu yok.)</summary>
    public class PlanHaritasi : MonoBehaviour
    {
        public ARPlaneManager duzlem;
        public Camera arKamera;
        public bool Hazir { get; private set; }
        public readonly List<Vector3> Iz = new List<Vector3>();
        public readonly Dictionary<int, IsaretBilgi> Isaretler = new Dictionary<int, IsaretBilgi>();
        public readonly List<Vector3> Kristaller = new List<Vector3>();
        Vector3 sonIzKonum; bool izVar;

        public void Temizle() { Iz.Clear(); Isaretler.Clear(); Kristaller.Clear(); Hazir = false; izVar = false; }

        public void Baslat(Vector3 konum) { Hazir = true; }

        void Update()
        {
            if (!Hazir || arKamera == null) return;
            var kp = arKamera.transform.position;
            if (ArApp.Ornek != null && ArApp.Ornek.Takipte && (!izVar || (kp - sonIzKonum).sqrMagnitude > 0.08f * 0.08f))
            { sonIzKonum = kp; izVar = true; Iz.Add(kp); }
        }
    }
}
