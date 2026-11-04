using System;
using ARaki.Net;
using UnityEngine;
using UnityEngine.UI;

namespace ARaki.Drive
{
    /// <summary>Kokpitin küçük parçaları: teker teker güç şasisi, teyp (saat + şarkı), dokunmatik direksiyon.</summary>
    public partial class DriveCockpit
    {
        // ---------------------------------------------------------------- teker teker güç

        /// <summary>
        /// Teker teker güç: vites kutusunun iki yanında, üstten görünen bir aracın paletleri gibi iki çubuk (sol / sağ
        /// teker). Çubuk ortadan yukarı dolarsa o yan ileri, aşağı dolarsa geri döner; yerinde dönüşte biri yukarı,
        /// öbürü aşağı dolar. c: vites kutusunun ortası.
        /// </summary>
        void Chassis(VectorShape body, Vector2 c)
        {
            const float tw = 7f, th = 60f, dx = 30.5f;
            var line = new Color(1f, 1f, 1f, 0.1f);
            for (int side = -1; side <= 1; side += 2)
            {
                var tc = c + V(side * dx, 0f);
                body.RoundRect(new Rect(tc.x - tw / 2f - 1f, tc.y - th / 2f - 1f, tw + 2f, th + 2f), 3.5f, line);
                body.RoundRect(new Rect(tc.x - tw / 2f, tc.y - th / 2f, tw, th), 2.5f, Hex("#0B0D0F"));
                for (float y = -th / 2f + 3f; y < th / 2f - 1f; y += 4f)   // palet dişleri
                    body.Line(V(tc.x - tw / 2f + 1.2f, tc.y + y), V(tc.x + tw / 2f - 1.2f, tc.y + y), 1f, new Color(1f, 1f, 1f, 0.045f));
                body.Line(V(tc.x - tw / 2f - 2.5f, tc.y), V(tc.x + tw / 2f + 2.5f, tc.y), 1f, new Color(1f, 1f, 1f, 0.22f));   // sıfır çizgisi
                var fill = Shape(side < 0 ? "Sol teker gücü" : "Sağ teker gücü", dash);
                var rt = (RectTransform)fill.transform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.pivot = Vector2.zero;
                rt.anchoredPosition = tc;
                rt.sizeDelta = Vector2.zero;
                fill.RoundRect(new Rect(-tw / 2f + 1.5f, 0.5f, tw - 3f, th / 2f - 2f), 1.5f, Color.white);
                fill.color = Accent;
                rt.localScale = new Vector3(1f, 0f, 1f);
                if (side < 0) { trackL = rt; trackFillL = fill; } else { trackR = rt; trackFillR = fill; }
            }
        }

        void UpdateChassis(RobotStatus.Motor m, float dt)
        {
            if (trackL == null) return;
            float k = Mathf.Min(1f, dt * 12f);
            trackShownL += ((m != null ? m.sol / 255f : 0f) - trackShownL) * k;
            trackShownR += ((m != null ? m.sag / 255f : 0f) - trackShownR) * k;
            void Show(RectTransform rt, VectorShape fill, float v)
            {
                v = Mathf.Clamp(v, -1f, 1f);
                rt.localScale = new Vector3(1f, Mathf.Abs(v) < 0.01f ? 0f : v, 1f);   // eksi: ortadan aşağı (geri)
                fill.color = v < 0f ? Red : Accent;
            }
            Show(trackL, trackFillL, trackShownL);
            Show(trackR, trackFillR, trackShownR);
        }

        // ---------------------------------------------------------------- teyp

        static readonly Color Lcd = Hex("#FFB14A");

        /// <summary>
        /// Teyp: eski araç teyplerindeki gibi kaset ağzı ve turuncu ışıklı ekran. Ekranda dijital saat, çalan şarkı
        /// (uzunsa kayar; çalmıyorken tarih) ve çalarken oynayan ekolayzır çubukları. Basınca şarkı listesi açılır.
        /// </summary>
        void BuildDeck(Rect r)
        {
            var b = Button("TEYP", r, () => { bool o = !drive.MusicMenuOpen; drive.CloseMenus(); drive.MusicMenuOpen = o; menuOpen = false; },
                           null, () => drive.MusicMenuOpen, 9, parent: dash, style: ButtonStyle.Deck);
            var rt = b.Rt;
            var lcdRect = new Rect(8f, 17f, r.width - 16f, 31f);
            var lcd = Shape("Ekran", rt);
            lcd.RoundRect(new Rect(lcdRect.x - 1f, lcdRect.y - 1f, lcdRect.width + 2f, lcdRect.height + 2f), 6f, new Color(0f, 0f, 0f, 0.9f));
            lcd.RoundRect(lcdRect, 5f, Hex("#140E07"));
            lcd.RoundRect(new Rect(lcdRect.x + 1f, lcdRect.yMax - 9f, lcdRect.width - 2f, 8f), 4f, new Color(1f, 0.7f, 0.3f, 0.035f));
            deckClock = Label(rt, "00:00", mono, 14, Lcd, TextAnchor.MiddleLeft, V(lcdRect.x + 6f + 24f, lcdRect.y + 22f), V(48, 16));
            // çalarken oynayan ekolayzır çubukları (sağ üstte)
            eqBars = new RectTransform[5];
            for (int i = 0; i < eqBars.Length; i++)
            {
                var bar = Shape("Ekolayzır", rt);
                var brt = (RectTransform)bar.transform;
                brt.anchorMin = brt.anchorMax = Vector2.zero;
                brt.pivot = Vector2.zero;
                brt.anchoredPosition = V(lcdRect.xMax - 34f + i * 5.5f, lcdRect.y + 15f);
                brt.sizeDelta = Vector2.zero;
                bar.RoundRect(new Rect(0f, 0f, 3.5f, 12f), 1f, Color.white);
                bar.color = new Color(Lcd.r, Lcd.g, Lcd.b, 0.85f);
                brt.localScale = new Vector3(1f, 0.15f, 1f);
                eqBars[i] = brt;
            }
            // şarkı adı kayan yazı: kutunun dışı kırpılır
            deckSongBox = Rt("Şarkı kutusu", rt, V(lcdRect.x + 5f, lcdRect.y + 3f), V(lcdRect.width - 10f, 11f));
            deckSongBox.gameObject.AddComponent<RectMask2D>();
            deckSong = Label(deckSongBox, "", font, 10, new Color(Lcd.r, Lcd.g, Lcd.b, 0.85f), TextAnchor.MiddleLeft, V(200f, 5.5f), V(400, 11));
        }

        static readonly string[] Days = { "PAZ", "PZT", "SAL", "ÇAR", "PER", "CUM", "CMT" };
        static readonly string[] Months = { "OCA", "ŞUB", "MAR", "NİS", "MAY", "HAZ", "TEM", "AĞU", "EYL", "EKİ", "KAS", "ARA" };

        void UpdateDeck(float dt)
        {
            if (deckClock == null) return;
            var now = DateTime.Now;
            deckClock.text = now.ToString("HH:mm");
            int playing = drive.PlayingMelody;
            bool on = playing > 0 && playing <= drive.Firmware.Songs.Count;
            string song = on ? drive.Firmware.Songs[playing - 1].Title : Days[(int)now.DayOfWeek] + " " + now.Day + " " + Months[now.Month - 1];
            if (deckSong.text != song) { deckSong.text = song; deckSong.rectTransform.anchoredPosition = V(0f, 0f); }
            float room = deckSongBox.rect.width, need = deckSong.preferredWidth, x = 0f;
            if (need > room)   // uzun ad: bir uca, bekle, öbür uca
                x = Mathf.Clamp(Mathf.PingPong(Time.unscaledTime * 18f, need - room + 24f) - 12f, 0f, need - room);
            deckSong.rectTransform.anchoredPosition = V(-x, 0f);
            // ekolayzır: çalarken her çubuk kendi hızında oynar, dururken alçak
            for (int i = 0; i < eqBars.Length; i++)
            {
                float h = on ? 0.25f + 0.75f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * (5.3f + i * 1.7f) + i * 1.1f)) : 0.15f;
                var sc = eqBars[i].localScale;
                eqBars[i].localScale = new Vector3(1f, Mathf.Lerp(sc.y, h, Mathf.Min(1f, dt * 14f)), 1f);
            }
        }

        // ---------------------------------------------------------------- görüntü kalitesi

        /// <summary>Üst satır için kısa kalite adı: köprü bit hızını ağa göre düşürüp yükseltir (uyarlanabilir görüntü).</summary>
        string VideoQuality(RobotStatus st)
        {
            if (drive.Link.Demo) return "görüntü demo";
            if (video.ActivePath == "JPEG") return "görüntü JPEG";
            if (st == null || !video.HasFrame) return "görüntü yok";
            if (st.hedef_kbps <= 0) return "görüntü H.264";
            string level = st.hedef_kbps >= 3500 ? "yüksek" : st.hedef_kbps >= 2200 ? "orta" : "düşük";
            return "görüntü " + level;
        }

        // ---------------------------------------------------------------- dokunmatik direksiyon

        bool AnyPanelOpen => menuOpen || drive.SettingsOpen || drive.MusicMenuOpen || drive.HornMenuOpen || drive.RobotPanelOpen;

        Vector2 WheelCenter() => root.InverseTransformPoint(wheelRt.position);

        /// <summary>Parmağın simide göre açısı: tepeden saat yönünde derece.</summary>
        static float PointerAngle(Vector2 p, Vector2 c) => Mathf.Atan2(p.x - c.x, p.y - c.y) * Mathf.Rad2Deg;

        /// <summary>
        /// Simit tutuldu mu: yalnızca "Direksiyon" dokunmatik modunda, göbeğin (korna) dışında, simidin biraz dışına
        /// kadar. Tutulan yer değil, sonraki dönüş sayılır; böylece simidin neresinden tutulursa tutulsun zıplamaz.
        /// </summary>
        bool TryGrabWheel(int id, Vector2 p)
        {
            if (drive.Settings.Touch != TouchMode.Wheel || wheelRt == null || AnyPanelOpen) return false;
            var c = WheelCenter();
            float d = (p - c).magnitude / dash.localScale.x;
            if (d < 50f || d > 195f) return false;
            grab.Pointer = id;
            grab.LastAngle = PointerAngle(p, c);
            grab.Angle = wheelAngle;
            return true;
        }

        void MoveWheel(Vector2 p)
        {
            var c = WheelCenter();
            // merkeze çok yakın açı ölçümü titrer: orada simit olduğu yerde kalır
            if ((p - c).magnitude / dash.localScale.x < 28f) return;
            float a = PointerAngle(p, c);
            grab.Angle = Mathf.Clamp(grab.Angle + Mathf.DeltaAngle(grab.LastAngle, a), -WheelLock, WheelLock);
            grab.LastAngle = a;
            // ortada ölü bölge; sonrası hafif üstel: küçük açılarda ince ayar, sonlara doğru keskin
            float x = Mathf.Clamp01((Mathf.Abs(grab.Angle) - WheelDead) / (WheelLock - WheelDead));
            drive.TouchSteer = Mathf.Sign(grab.Angle) * (0.5f * x + 0.5f * x * x);
        }

        void ReleaseWheel()
        {
            grab.Pointer = int.MinValue;
            drive.TouchSteer = null;   // direksiyon tuştaki gibi kendiliğinden ortaya döner
        }
    }
}
