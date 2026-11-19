using UnityEngine;

namespace ArTarama
{
    /// <summary>12 yaw sektoru x 3 kategori (goz hizi, alcak, tavan) kapsama takibi.</summary>
    public class KapsamaTakip
    {
        public const int Sektor = 12;
        public const int Goz = 0, Alcak = 1, Tavan = 2;
        public readonly bool[][] Dolu = { new bool[Sektor], new bool[Sektor], new bool[Sektor] };
        public int SonSektor = -1;

        public static int SektorBul(Vector3 ileri)
        {
            float yaw = Mathf.Atan2(ileri.x, ileri.z) * Mathf.Rad2Deg;
            if (yaw < 0) yaw += 360f;
            return Mathf.Clamp((int)(yaw / 30f), 0, Sektor - 1);
        }

        public void Isle(Vector3 konum, Vector3 ileri, bool zeminVar, float zeminY)
        {
            int s = SektorBul(ileri);
            SonSektor = s;
            float pitch = Mathf.Asin(Mathf.Clamp(ileri.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (pitch > 35f) { Dolu[Tavan][s] = true; return; }
            if (!zeminVar) return;
            float h = konum.y - zeminY;
            if (h < 0.40f) Dolu[Alcak][s] = true;
            else if (h > 0.90f) Dolu[Goz][s] = true;
        }

        public int Say(int kat) { int n = 0; foreach (var b in Dolu[kat]) if (b) n++; return n; }
        public float Oran(int kat) { return Say(kat) / (float)Sektor; }
        public void Sifirla() { for (int k = 0; k < 3; k++) System.Array.Clear(Dolu[k], 0, Sektor); }
    }
}
