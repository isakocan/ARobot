using System;
using System.Collections.Generic;

namespace ArTarama
{
    public enum KocSimge { Bilgi, Uyari, Egim, Don, Yuru, Tamam, Bekle }

    public class KocGirdi
    {
        public bool takipte, oncedenTakip;
        public int neden;              // 0 yok, 1 asiri hareket, 2 isik az, 3 ozellik az, 4 yeniden konumlaniyor, 5 baslatiliyor
        public bool hizli;             // acisal / dogrusal hiz yuksek
        public float ortancaDerinlik = -1f, guvenOrani = -1f;
        public float kx, ky, kz, yaw, pitch;   // yaw: derece [0,360), sagA donunce artar; pitch: yukari +
        public float zaman;
        public OdaSonuc sonuc;
    }

    public class KocMesaj
    {
        public string yazi = ""; public KocSimge simge = KocSimge.Bilgi; public bool uyari;
        public int donus;               // -1 sola, +1 saga (kenar oku)
        public bool okVar; public float okYaw;   // ilerleme oku: dunya yaw'i (derece)
        public int kural;               // 1..7 (7: oda tamam), 0 varsayilan/baslangic
    }

    /// <summary>
    /// Yonlendirme kocu (saf C#, test edilebilir): tek satirlik kibar mesaj, oncelik sirasina gore, en az 1,5 sn aralikla degisir.
    /// 1 izleme sorunu, 2 mesafe, 3 "bekle", 4 zemin/tavan egimi, 7 oda tamam, 5 yon kapsama, 6 yer degistir.
    /// </summary>
    public class OdaKoc
    {
        public const int Bin = 16;
        public const float Debounce = 1.5f;
        public readonly bool[] Gorulen = new bool[Bin];
        public int GorulenSayi { get { int n = 0; foreach (var b in Gorulen) if (b) n++; return n; } }
        public KocMesaj Mevcut = new KocMesaj();
        float baslangic = float.NaN, sonDegisim = -100f, sonOnay = 0f; int sonOnaySayi;
        bool ankorVar; float ax, az;
        readonly Dictionary<int, float[]> ilerleme = new Dictionary<int, float[]>();   // id -> [son ilerleme, son degisim zamani]
        public int YeniKonumSayisi;

        public const string Baslangic = "Telefonu göğüs hizasında tut ve odanın bir köşesine doğrult";

        public void Sifirla()
        {
            Array.Clear(Gorulen, 0, Bin); baslangic = float.NaN; sonDegisim = -100f; sonOnay = 0; sonOnaySayi = 0; ankorVar = false;
            ilerleme.Clear(); Mevcut = new KocMesaj(); YeniKonumSayisi = 0;
        }

        static double Fark(double a, double b) { double d = ((a - b) % 360 + 540) % 360 - 180; return d; }

        void Kapsama(KocGirdi g)
        {
            if (!ankorVar) { ankorVar = true; ax = g.kx; az = g.kz; }
            float dx = g.kx - ax, dz = g.kz - az;
            if (dx * dx + dz * dz > 1.5f * 1.5f) { ax = g.kx; az = g.kz; Array.Clear(Gorulen, 0, Bin); YeniKonumSayisi++; }
            if (!g.takipte || Math.Abs(g.pitch) > 50f) return;
            for (int b = 0; b < Bin; b++)
                if (Math.Abs(Fark(g.yaw, (b + 0.5) * 360.0 / Bin)) <= 14.0) Gorulen[b] = true;
        }

        public KocMesaj Guncelle(KocGirdi g)
        {
            if (float.IsNaN(baslangic)) baslangic = g.zaman;
            Kapsama(g);
            var s = g.sonuc;
            if (s != null)
            {
                int say = s.KesinDuvar + s.KesinKutu;
                if (say > sonOnaySayi) sonOnay = g.zaman;
                sonOnaySayi = say;
            }
            var aday = Sec(g);
            if (Mevcut.yazi != aday.yazi && g.zaman - sonDegisim >= Debounce) { Mevcut = aday; sonDegisim = g.zaman; }
            else if (Mevcut.yazi == aday.yazi) { Mevcut.donus = aday.donus; Mevcut.okVar = aday.okVar; Mevcut.okYaw = aday.okYaw; }
            return Mevcut;
        }

        KocMesaj M(string y, KocSimge i, int kural, bool uyari = false) { return new KocMesaj { yazi = y, simge = i, kural = kural, uyari = uyari }; }

        KocMesaj Sec(KocGirdi g)
        {
            var s = g.sonuc;
            // 1) izleme sorunlari
            if (!g.takipte)
            {
                if (!g.oncedenTakip && g.neden != 1 && g.neden != 2) return M(Baslangic, KocSimge.Bilgi, 0);
                switch (g.neden)
                {
                    case 1: return M("Daha yavaş hareket et", KocSimge.Uyari, 1, true);
                    case 2: return M("Ortam biraz karanlık, ışığı açabilirsin", KocSimge.Uyari, 1, true);
                    case 3: return M("Kamerayı eşyaların olduğu yere çevir", KocSimge.Uyari, 1, true);
                    default: return M("Kamerayı az önceki yere çevir", KocSimge.Uyari, 1, true);
                }
            }
            if (g.hizli) return M("Daha yavaş hareket et", KocSimge.Uyari, 1, true);

            // 2) mesafe (yerde/tavanda bakarken uyarma)
            if (Math.Abs(g.pitch) < 40f && g.ortancaDerinlik > 0f)
            {
                // Not: dusuk derinlik guveni beyaz duvarlarda olagan (gercek taramada medyan 8/255); "yaklas" icin kullanilmaz.
                if (g.ortancaDerinlik < 0.5f) return M("Biraz geri çekil", KocSimge.Yuru, 2);
                if (g.ortancaDerinlik > 4.5f) return M("Duvara biraz yaklaş", KocSimge.Yuru, 2);
            }

            // 3) onaya yakin aday gorus alaninda
            if (s != null)
            {
                string bekle = null;
                foreach (var d in s.duvarlar)
                    if (!d.kesin && d.ilerleme >= 0.5f && Gorunur(g, (d.ax + d.bx) * 0.5f, (d.az + d.bz) * 0.5f)) { bekle = Takili(g, 100000 + d.id, d.ilerleme); break; }
                if (bekle == null)
                    foreach (var k in s.kutular)
                        if (!k.kesin && k.ilerleme >= 0.5f && Gorunur(g, k.cx, k.cz)) { bekle = Takili(g, 200000 + k.id, k.ilerleme); break; }
                if (bekle != null) return M(bekle, bekle.StartsWith("Biraz") ? KocSimge.Yuru : KocSimge.Bekle, 3);
            }

            // 4) zemin / tavan (yalniz egme)
            if (s == null || !s.zeminVar) return M("Telefonu hafifçe zemine doğru eğ", KocSimge.Egim, 4);
            if (!s.tavanGercek && s.duvarKapsamaTum >= 0.4f && s.duvarlar.Count >= 2) return M("Telefonu biraz yukarı eğ, tavanı tara", KocSimge.Egim, 4);

            // 7) oda tamam (5 ve 6'dan once: oda bittiyse yon/yer onerisi anlamsiz)
            if (s.dongu || s.duvarKapsamaKesin >= 0.85f) return M("Oda tamamlanmak üzere. Bitirmek için Bitir'e dokun.", KocSimge.Tamam, 7);

            // baslangic metni: ilk saniyeler
            if (g.zaman - baslangic < 8f && GorulenSayi < 3 && s.KesinDuvar == 0) return M(Baslangic, KocSimge.Bilgi, 0);

            // 5) yon kapsama: duvari HENUZ BULUNMAMIS yonler (telefonun dondugu acilar degil; s.yonDuvar)
            if (s.yonDuvar != null && s.yonDuvar.Length == Bin)
            {
                double en = 1e9, enFark = 0; int eksik = 0;
                for (int b = 0; b < Bin; b++)
                {
                    if (s.yonDuvar[b]) continue;
                    eksik++;
                    double f = Fark((b + 0.5) * 360.0 / Bin, g.yaw);
                    if (Math.Abs(f) < en) { en = Math.Abs(f); enFark = f; }
                }
                if (eksik > 0)
                {
                    if (en <= 25) return M("Bu taraf taranıyor, telefonu yavaşça gezdir", KocSimge.Bekle, 5);
                    if (en >= 135) { var ma = M("Arkana dön, o taraftaki duvar henüz bulunmadı", KocSimge.Don, 5); ma.donus = enFark >= 0 ? 1 : -1; return ma; }
                    var m = M(enFark >= 0 ? "Sağa dön, o taraftaki duvar henüz bulunmadı" : "Sola dön, o taraftaki duvar henüz bulunmadı", KocSimge.Don, 5);
                    m.donus = enFark >= 0 ? 1 : -1; return m;
                }
            }

            // 6) yerel kapsama doygun: kesfedilmemis yone ilerle
            if (g.zaman - sonOnay >= 6f && s.sinir.Length >= 2)
            {
                double en = 1e9; float ex = 0, ez = 0; bool var = false;
                for (int i = 0; i + 1 < s.sinir.Length; i += 2)
                {
                    double dx = s.sinir[i] - g.kx, dz = s.sinir[i + 1] - g.kz, d = Math.Sqrt(dx * dx + dz * dz);
                    if (d < 1.2 || d >= en) continue;
                    en = d; ex = s.sinir[i]; ez = s.sinir[i + 1]; var = true;
                }
                if (var)
                {
                    var m = M("Odanın diğer tarafına doğru ilerle", KocSimge.Yuru, 6);
                    m.okVar = true; m.okYaw = (float)(Math.Atan2(ex - g.kx, ez - g.kz) * 180.0 / Math.PI); if (m.okYaw < 0) m.okYaw += 360f;
                    return m;
                }
            }
            return M("Yavaşça gezinmeye devam et", KocSimge.Bilgi, 0);
        }

        static bool Gorunur(KocGirdi g, float x, float z)
        {
            double dx = x - g.kx, dz = z - g.kz; double d = Math.Sqrt(dx * dx + dz * dz);
            if (d > 4.5 || d < 0.2) return false;
            double yaw = Math.Atan2(dx, dz) * 180.0 / Math.PI;
            return Math.Abs(Fark(yaw, g.yaw)) < 25.0 && Math.Abs(g.pitch) < 50f;
        }

        /// <summary>Ilerleme 6 sn boyunca ilerlemediyse "yana yuru" onerir (bakis yonu eksik).</summary>
        string Takili(KocGirdi g, int anahtar, float ilr)
        {
            if (!ilerleme.TryGetValue(anahtar, out var v)) { ilerleme[anahtar] = new[] { ilr, g.zaman }; return "Burada bir saniye bekle"; }
            if (ilr > v[0] + 0.02f) { v[0] = ilr; v[1] = g.zaman; return "Burada bir saniye bekle"; }
            return g.zaman - v[1] > 6f ? "Biraz yana doğru yürü" : "Burada bir saniye bekle";
        }
    }
}
