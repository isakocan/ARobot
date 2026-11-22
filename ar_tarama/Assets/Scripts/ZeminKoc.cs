using System;

namespace ArTarama
{
    /// <summary>
    /// Acik zemin taramasi icin yonlendirme (saf C#). Yazi yalniz sorun varken: 1 izleme sorunu / hizli, 2 cok yakin,
    /// 3 zemin henuz yok. Gerisi yazisiz: zeminde kucuk bir isaret taranmamis zemine gosterir (hedefVar).
    /// </summary>
    public class ZeminKoc
    {
        public const float Debounce = 1.5f;
        public KocMesaj Mevcut = new KocMesaj();
        float baslangic = float.NaN, sonDegisim = -100f;

        public const string Baslangic = "Telefonu göğüs hizasında tut ve hafifçe zemine doğru eğ";

        public void Sifirla() { baslangic = float.NaN; sonDegisim = -100f; Mevcut = new KocMesaj(); }

        static double Fark(double a, double b) { double d = ((a - b) % 360 + 540) % 360 - 180; return d; }
        static KocMesaj M(string y, KocSimge i, int kural, bool uyari = false) { return new KocMesaj { yazi = y, simge = i, kural = kural, uyari = uyari }; }

        public KocMesaj Guncelle(KocGirdi g, ZeminSonuc s)
        {
            if (float.IsNaN(baslangic)) baslangic = g.zaman;
            var aday = Sec(g, s);
            if (Mevcut.yazi != aday.yazi && g.zaman - sonDegisim >= Debounce) { Mevcut = aday; sonDegisim = g.zaman; }
            else if (Mevcut.yazi == aday.yazi) { Mevcut.donus = aday.donus; Mevcut.okVar = aday.okVar; Mevcut.okYaw = aday.okYaw; }
            // isaret yazidan bagimsiz, hemen guncellenir (ayni hedefte kalmak icin 30 cm'den az degisim yok sayilir)
            if (!aday.hedefVar) Mevcut.hedefVar = false;
            else if (!Mevcut.hedefVar || (aday.hx - Mevcut.hx) * (aday.hx - Mevcut.hx) + (aday.hz - Mevcut.hz) * (aday.hz - Mevcut.hz) > 0.09f)
            { Mevcut.hedefVar = true; Mevcut.hx = aday.hx; Mevcut.hz = aday.hz; }
            return Mevcut;
        }

        KocMesaj Sec(KocGirdi g, ZeminSonuc s)
        {
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
            if (g.ortancaDerinlik > 0f && g.ortancaDerinlik < 0.4f && g.pitch > -50f) return M("Biraz geri çekil", KocSimge.Yuru, 2);
            if (s == null || !s.zeminVar || s.acik.Count < 40) return M(g.pitch > -20f ? "Telefonu hafifçe zemine doğru eğ" : "Zemin aranıyor, telefonu yavaşça gezdir", KocSimge.Egim, 3);
            if (g.zaman - baslangic < 4f) return M(Baslangic, KocSimge.Bilgi, 0);

            // Kullanici istegi (21 Kasim): surekli yazi yok; boyanan zeminden neyin tarandigi anlasiliyor. Yalniz eksik
            // yerin uzerine zeminde kucuk bir isaret: kullanicinin onune en yakin (0,8-6 m) taranmamis zemin siniri.
            double ed = 1e9; float ex = 0, ez = 0; bool var = false;
            for (int i = 0; i + 1 < s.sinir.Length; i += 2)
            {
                double dx = s.sinir[i] - g.kx, dz = s.sinir[i + 1] - g.kz, d = Math.Sqrt(dx * dx + dz * dz);
                if (d < 0.8 || d > 6) continue;
                // onundekine oncelik: arkadaki 2 kat uzak sayilir
                double fark = Math.Abs(Fark(Math.Atan2(dx, dz) * 180.0 / Math.PI, g.yaw));
                double m = d * (1.0 + fark / 180.0);
                if (m >= ed) continue;
                ed = m; ex = s.sinir[i]; ez = s.sinir[i + 1]; var = true;
            }
            var sonuc = M("", KocSimge.Bilgi, var ? 6 : 7);
            if (var) { sonuc.hedefVar = true; sonuc.hx = ex; sonuc.hz = ez; }
            return sonuc;
        }
    }
}
