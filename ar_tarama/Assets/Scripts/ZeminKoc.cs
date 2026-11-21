using System;

namespace ArTarama
{
    /// <summary>
    /// Acik zemin taramasi icin yonlendirme (saf C#). Tek satir, kibar, en az 1,5 sn aralikla degisir. Oncelik:
    /// 1 izleme sorunu, 2 cok yakin, 3 zemin henuz yok, 8 cevrede taranmamis zemin (don / asagi eg), 6 kesif siniri (yuru),
    /// 7 cevre tamam (baska odaya gec ya da bitir). Cok odali: "oda bitti" yerine "bu cevre tamam" der.
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

            // 8) cevrede taranmamis zemin: en yakin (ve cok eksik olan) yon
            double en = 1e9, enFark = 0; float enOran = 0;
            for (int b = 0; b < 16; b++)
            {
                float o = s.yonEksik[b]; if (o < 0.4f) continue;
                double f = Fark((b + 0.5) * 22.5, g.yaw);
                double maliyet = Math.Abs(f) - 40.0 * o;
                if (maliyet < en) { en = maliyet; enFark = f; enOran = o; }
            }
            if (enOran > 0f)
            {
                if (Math.Abs(enFark) <= 30)
                    return g.pitch > -25f ? M("Telefonu biraz aşağı eğ, önündeki zemini göster", KocSimge.Egim, 8)
                                          : M("Zemin taranıyor, telefonu yavaşça sağa sola gezdir", KocSimge.Bekle, 8);
                var m = M(Math.Abs(enFark) >= 135 ? "Arkana dön, oradaki zemin henüz taranmadı" : enFark >= 0 ? "Sağa dön, oradaki zemin henüz taranmadı" : "Sola dön, oradaki zemin henüz taranmadı", KocSimge.Don, 8);
                m.donus = enFark >= 0 ? 1 : -1; return m;
            }

            // 6) kesif siniri: 1,2 m'den uzak en yakin taranmamis kenara yuru
            double ed = 1e9; float ex = 0, ez = 0; bool var = false;
            for (int i = 0; i + 1 < s.sinir.Length; i += 2)
            {
                double dx = s.sinir[i] - g.kx, dz = s.sinir[i + 1] - g.kz, d = Math.Sqrt(dx * dx + dz * dz);
                if (d < 1.2 || d >= ed) continue;
                ed = d; ex = s.sinir[i]; ez = s.sinir[i + 1]; var = true;
            }
            if (var && ed < 8)
            {
                var m = M("Taranmamış zemine doğru yürü", KocSimge.Yuru, 6);
                m.okVar = true; m.okYaw = (float)(Math.Atan2(ex - g.kx, ez - g.kz) * 180.0 / Math.PI); if (m.okYaw < 0) m.okYaw += 360f;
                return m;
            }
            return M("Burası tamam. Başka odaya geçebilir ya da Bitir'e dokunabilirsin", KocSimge.Tamam, 7);
        }
    }
}
