using UnityEngine;

namespace ArTarama
{
    public enum YonTuru { Yok, Sol, Sag, Yukari, Asagi, Tara, Tamam }

    public struct Yonlendirme
    {
        public YonTuru tur; public string yazi; public float aci; public int hedefSektor;
    }

    /// <summary>
    /// Adim adim tarama akisi ve kapsama takibi. 12 yaw sektoru (30 derece) x 3 kategori (goz hizasi, tavan, alcak).
    /// Bir sektor, kamera o yonde yeterince yavas ve dogru yukseklik/egimdeyken ~0.7 sn bakilinca dolar.
    /// </summary>
    public class TaramaAkisi
    {
        public const int Sektor = 12, AdimSayisi = 5;
        public const int Zemin = 0, Goz = 1, Tavan = 2, Alcak = 3, Isaret = 4;
        public static readonly string[] Baslik = { "Zemini bul", "Göz hizasında tur", "Tavan", "Alçak tur", "İşaretler" };
        public static readonly string[] Aciklama =
        {
            "Telefonu yere doğru tut ve yavaşça gezdir",
            "Telefonu göz hizasında tut, odanın çevresinde dön",
            "Telefonu yukarı kaldır, tavanı tara",
            "Telefonu robot yüksekliğine indir, bir tur daha",
            "Basılı işaretlere sırayla bak"
        };
        public static readonly int[] Gerekli = { 0, 12, 6, 8, 0 };
        public const float SektorSure = 0.7f;
        public const float EsikGoz = 0.90f, EsikAlcak = 0.40f, TavanEgim = 35f;

        public int Mevcut;
        public bool[] Tamamlandi = new bool[AdimSayisi], Atlandi = new bool[AdimSayisi];
        // ilerleme[kategori] kategori: 0 goz, 1 tavan, 2 alcak
        public readonly float[][] Ilerleme = { new float[Sektor], new float[Sektor], new float[Sektor] };
        public float Yaw, Egim, Yukseklik;   // anlik
        public int SektorNo;
        float zeminSure;

        public static int KategoriDe(int adim) { return adim == Goz ? 0 : adim == Tavan ? 1 : adim == Alcak ? 2 : -1; }

        public void Sifirla()
        {
            Mevcut = 0; zeminSure = 0;
            for (int i = 0; i < AdimSayisi; i++) { Tamamlandi[i] = false; Atlandi[i] = false; }
            for (int k = 0; k < 3; k++) System.Array.Clear(Ilerleme[k], 0, Sektor);
        }

        public int Say(int kat) { int n = 0; foreach (var v in Ilerleme[kat]) if (v >= 1f) n++; return n; }

        /// <summary>Adim ilerlemesi 0..1.</summary>
        public float AdimIlerleme(int adim, float isaretOrani)
        {
            if (Tamamlandi[adim]) return 1f;
            int k = KategoriDe(adim);
            if (k >= 0)
            {
                float t = 0;
                foreach (var v in Ilerleme[k]) t += Mathf.Clamp01(v);
                return Mathf.Clamp01(t / Gerekli[adim]);
            }
            if (adim == Zemin) return Mathf.Clamp01(zeminSure / 0.6f);
            return isaretOrani;
        }

        public static void Durum(Transform kam, out float yaw, out float egim)
        {
            Vector3 f = kam.forward;
            egim = Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 h = new Vector3(f.x, 0, f.z);
            if (h.magnitude < 0.35f)
            {
                Vector3 u = kam.up; Vector3 hu = new Vector3(u.x, 0, u.z);
                h = f.y > 0 ? -hu : hu;
            }
            yaw = Mathf.Atan2(h.x, h.z) * Mathf.Rad2Deg;
            if (yaw < 0) yaw += 360f;
        }

        public static int SektorBul(float yaw) { return Mathf.Clamp((int)(yaw / 30f), 0, Sektor - 1); }

        public bool KosulOk(int kat, float yukseklik, float egim, bool zeminVar)
        {
            switch (kat)
            {
                case 0: return zeminVar && yukseklik > EsikGoz && egim > -35f && egim < TavanEgim;
                case 1: return egim > TavanEgim;
                case 2: return zeminVar && yukseklik < EsikAlcak && egim < TavanEgim && egim > -60f;
                default: return false;
            }
        }

        /// <summary>Her karede cagrilir. Adim tamamlandiysa true doner (arayuz animasyon icin).</summary>
        public bool Isle(float dt, Transform kam, bool zeminVar, float zeminY, float aciHizi, bool takipte, float isaretOrani, bool isaretBitti)
        {
            Durum(kam, out Yaw, out Egim);
            Yukseklik = zeminVar ? kam.position.y - zeminY : 9f;
            SektorNo = SektorBul(Yaw);
            if (takipte && aciHizi < 60f)
            {
                for (int kat = 0; kat < 3; kat++)
                    if (KosulOk(kat, Yukseklik, Egim, zeminVar))
                        Ilerleme[kat][SektorNo] = Mathf.Min(1f, Ilerleme[kat][SektorNo] + dt / SektorSure);
            }
            if (zeminVar && takipte) zeminSure += dt; else zeminSure = Mathf.Max(0, zeminSure - dt * 0.5f);
            return false;
        }

        /// <summary>Su anki adim kosulu saglandiysa true.</summary>
        public bool AdimKosuluSaglandi(int adim, bool isaretBitti)
        {
            if (adim == Zemin) return zeminSure >= 0.6f;
            int k = KategoriDe(adim);
            if (k >= 0) return Say(k) >= Gerekli[adim];
            return isaretBitti;
        }

        public Yonlendirme Yon(int adim)
        {
            var y = new Yonlendirme { tur = YonTuru.Yok, yazi = "", hedefSektor = -1 };
            if (adim == Zemin)
            {
                if (Egim > -20f) { y.tur = YonTuru.Asagi; y.yazi = "Telefonu aşağı çevir"; }
                else { y.tur = YonTuru.Tara; y.yazi = "Zemini tarıyorum…"; }
                return y;
            }
            if (adim == Isaret) { y.yazi = "İşareti kadraja al"; return y; }
            int kat = KategoriDe(adim);
            // yukseklik / egim kosullari once
            if (adim == Goz)
            {
                if (Yukseklik < EsikGoz) { y.tur = YonTuru.Yukari; y.yazi = "Telefonu göz hizasına kaldır"; return y; }
                if (Egim > 30f) { y.tur = YonTuru.Asagi; y.yazi = "Biraz aşağı bak"; return y; }
                if (Egim < -30f) { y.tur = YonTuru.Yukari; y.yazi = "Biraz yukarı bak"; return y; }
            }
            else if (adim == Tavan)
            {
                if (Egim <= TavanEgim) { y.tur = YonTuru.Yukari; y.yazi = "Biraz yukarı bak"; return y; }
            }
            else if (adim == Alcak)
            {
                if (Yukseklik >= EsikAlcak) { y.tur = YonTuru.Asagi; y.yazi = "Telefonu aşağı indir"; return y; }
                if (Egim > 30f) { y.tur = YonTuru.Asagi; y.yazi = "Biraz aşağı bak"; return y; }
            }
            var ilr = Ilerleme[kat];
            if (ilr[SektorNo] < 1f)
            {
                y.tur = YonTuru.Tara; y.yazi = "Yavaşça tara…"; y.hedefSektor = SektorNo; return y;
            }
            // en yakin bos sektor
            float en = 1e9f; int hs = -1; float enAci = 0;
            for (int s = 0; s < Sektor; s++)
            {
                if (ilr[s] >= 1f) continue;
                float d = Mathf.DeltaAngle(Yaw, s * 30f + 15f);
                if (Mathf.Abs(d) < en) { en = Mathf.Abs(d); hs = s; enAci = d; }
            }
            if (hs < 0) { y.tur = YonTuru.Tamam; y.yazi = "Bu adım tamam"; return y; }
            y.hedefSektor = hs; y.aci = enAci;
            if (enAci >= 0) { y.tur = YonTuru.Sag; y.yazi = "Sağa dön"; }
            else { y.tur = YonTuru.Sol; y.yazi = "Sola dön"; }
            return y;
        }
    }
}
