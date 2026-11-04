using System;
using System.Collections.Generic;
using ARaki.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ARaki.Drive
{
    /// <summary>
    /// Robot paneli (menü › ROBOT): tarayıcıdaki robot panelinin işleri uygulamanın içinde. Bağlı ağ ve sinyal,
    /// Wi-Fi bandı, yakındaki ağlar (şifreyle bağlanma), kurulum ağı, yeniden başlatma ve kapatma. Ağı ya da bandı
    /// değiştirmek bağlantıyı bir süre kopardığı için bu düğmeler iki kez basınca çalışır (ilk basış ne olacağını söyler).
    /// </summary>
    public partial class DriveCockpit
    {
        const int NetRows = 6;
        readonly RobotPanelClient panel = new RobotPanelClient();
        Text robotInfo, robotMessage, passwordLabel;
        readonly Btn[] netButtons = new Btn[NetRows];
        readonly Text[] netTags = new Text[NetRows];
        RectTransform passwordRow;
        string chosenSsid, password = "", confirmKey;
        bool chosenNeedsPassword, passwordFocus, showPassword;
        float confirmUntil, messageUntil, lastPanelPoll = -100f;
        bool wasConnected;
        TouchScreenKeyboard touchKeyboard;
        bool textHooked;

        void OpenRobotPanel()
        {
            drive.RobotPanelOpen = true;
            chosenSsid = null;
            passwordFocus = false;
            lastPanelPoll = -100f;
        }

        void BuildRobotPanel()
        {
            var r = new Rect(W / 2f - 310f, 12f, 620f, Mathf.Min(640f, H - 12f - 80f));
            robotRoot = Panel("Robot", r);
            float L = 20f;   // panelin içinde sol kenar
            float y = r.height - 46f;
            Label(robotRoot, "ROBOT", font, 18, Text, TextAnchor.MiddleLeft, V(L + 150f, y + 14f), V(300, 24));
            y -= 54f;
            robotInfo = Label(robotRoot, "", mono, 13, Text, TextAnchor.UpperLeft, V(L + 290f, y + 4f), V(580, 52));
            y -= 40f;

            Label(robotRoot, "Wi-Fi bandı", font, 16, Text, TextAnchor.MiddleLeft, V(L + 170f, y + 14f), V(340, 22));
            string[] bands = { "", "bg", "a" }, bandNames = { "Otomatik", "2,4 GHz", "5 GHz" };
            for (int i = 0; i < 3; i++)
            {
                string band = bands[i], name = bandNames[i];
                Button(name, new Rect(r.xMax - 20f - (3 - i) * 92f, r.y + y, 88f, 28f),
                       () => Confirm("bant" + band, "Bant değişince bağlantı 15-40 sn kopar. Onaylamak için " + name + " düğmesine tekrar bas.",
                                     () => StartCoroutine(panel.Post(drive.Link.Host, "/api/bant", RobotPanelClient.Json("bant", band),
                                         (ok, e) => Say(ok ? "Bant değişiyor: robot " + name + " ile yeniden bağlanıyor, uygulama kendisi bulur." : e)))),
                       null, () => panel.Status != null && (panel.Status.bant ?? "") == band, 14, TextAnchor.MiddleCenter, robotRoot);
            }
            y -= 22f;
            Label(robotRoot, "2,4 GHz duvardan daha iyi geçer, 5 GHz daha hızlıdır.", font, 12, Dim, TextAnchor.MiddleLeft, V(L + 250f, y + 6f), V(500, 14));
            y -= 34f;

            Label(robotRoot, "Yakındaki ağlar", font, 16, Text, TextAnchor.MiddleLeft, V(L + 170f, y + 14f), V(340, 22));
            Button("YENİLE", new Rect(r.xMax - 20f - 100f, r.y + y, 100f, 28f),
                   () => { Say("Ağlar taranıyor (birkaç saniye; görüntü bir an takılabilir)…"); StartCoroutine(panel.Scan(drive.Link.Host)); },
                   null, () => panel.Scanning, 14, TextAnchor.MiddleCenter, robotRoot, () => panel.Scanning ? "TARANIYOR" : "YENİLE");
            y -= 32f;
            for (int i = 0; i < NetRows; i++)
            {
                int k = i;
                netButtons[i] = Button("", new Rect(r.x + L, r.y + y, r.width - 2f * L, 27f), () => ChooseNetwork(k), null,
                                       () => NetworkAt(k)?.ssid != null && NetworkAt(k).ssid == panel.Status?.ag, 15, TextAnchor.MiddleLeft, robotRoot,
                                       () => NetworkAt(k)?.ssid ?? "");
                netTags[i] = Label(robotRoot, "", mono, 12, Dim, TextAnchor.MiddleRight, V(r.width - L - 110f - 10f, y + 13.5f), V(220, 16));
                y -= 30f;
            }

            // şifre / onay satırı (bir ağ seçilince)
            y -= 4f;
            // satırın düğmeleri satıra göre konur (Button, ebeveynin konumunu çıkarır): x, y panelin içinde
            passwordRow = Rt("Şifre satırı", robotRoot, V(0f, y), V(r.width, 30f));
            passwordLabel = Label(passwordRow, "", font, 14, Text, TextAnchor.MiddleLeft, V(L + 110f, 14f), V(220, 18));
            Button("", new Rect(L + 228f, y, 170f, 28f), () => FocusPassword(), null, () => passwordFocus, 15,
                   TextAnchor.MiddleLeft, passwordRow, () => PasswordShown());
            Button("GÖSTER", new Rect(L + 404f, y, 60f, 28f), () => showPassword = !showPassword, null, () => showPassword, 11,
                   TextAnchor.MiddleCenter, passwordRow);
            Button("BAĞLAN", new Rect(r.width - L - 116f, y, 60f, 28f), ConnectChosen, null, null, 12, TextAnchor.MiddleCenter, passwordRow);
            Button("VAZGEÇ", new Rect(r.width - L - 52f, y, 52f, 28f), () => { chosenSsid = null; passwordFocus = false; }, null, null, 11,
                   TextAnchor.MiddleCenter, passwordRow);
            y -= 46f;

            Label(robotRoot, "Kurulum ve güç", font, 16, Text, TextAnchor.MiddleLeft, V(L + 170f, y + 14f), V(340, 22));
            Button("KURULUM AĞI", new Rect(r.xMax - 20f - 3 * 128f + 8f, r.y + y, 120f, 28f),
                   () => Confirm("kurulum", "Robot ev ağından çıkıp kendi ağını açar. Onaylamak için tekrar bas.",
                                 () => StartCoroutine(panel.Post(drive.Link.Host, "/api/kurulum-modu", "{}",
                                     (ok, e) => Say(ok ? "Robot kendi ağını açıyor: telefonu “" + (panel.Status?.kurulum_agi ?? "robot") +
                                                         "” ağına bağla, açılan sayfadan yeni ağı seç." : e)))),
                   null, null, 12, TextAnchor.MiddleCenter, robotRoot);
            Button("YENİDEN BAŞLAT", new Rect(r.xMax - 20f - 2 * 128f + 8f, r.y + y, 120f, 28f),
                   () => Confirm("yeniden", "Robot yeniden başlar (~1 dk). Onaylamak için tekrar bas.",
                                 () => StartCoroutine(panel.Post(drive.Link.Host, "/api/guc", RobotPanelClient.Json("islem", "yeniden"),
                                     (ok, e) => Say(ok ? "Robot yeniden başlıyor; açılınca uygulama kendisi bağlanır." : e)))),
                   null, null, 12, TextAnchor.MiddleCenter, robotRoot);
            var off = Button("KAPAT", new Rect(r.xMax - 20f - 128f + 8f, r.y + y, 120f, 28f),
                   () => Confirm("kapat", "Robot kapanır. Onaylamak için tekrar bas.",
                                 () => StartCoroutine(panel.Post(drive.Link.Host, "/api/guc", RobotPanelClient.Json("islem", "kapat"),
                                     (ok, e) => Say(ok ? "Robot kapanıyor: 20 sn bekleyip gücü kesebilirsin." : e)))),
                   null, null, 12, TextAnchor.MiddleCenter, robotRoot);
            off.OnColor = new Color(Red.r * 0.45f, Red.g * 0.45f, Red.b * 0.45f, 0.9f);
            y -= 36f;
            robotMessage = Label(robotRoot, "", font, 14, Amber, TextAnchor.UpperLeft, V(L + 290f, y - 6f), V(580, 40));
            robotMessage.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        PanelStatus.Network NetworkAt(int i)
        {
            var a = panel.Status?.aglar;
            return a != null && i < a.Length ? a[i] : null;
        }

        /// <summary>İlk basış ne olacağını söyler, 4 sn içinde ikinci basış yapar.</summary>
        void Confirm(string key, string question, Action act)
        {
            if (drive.Link.Demo || string.IsNullOrEmpty(drive.Link.Host)) { Say("Robot paneli robota bağlıyken çalışır."); return; }
            if (confirmKey == key && Time.unscaledTime < confirmUntil) { confirmKey = null; act(); return; }
            confirmKey = key;
            confirmUntil = Time.unscaledTime + 4f;
            Say(question, 4f);
        }

        void Say(string text, float seconds = 12f)
        {
            if (robotMessage == null) return;
            robotMessage.text = text;
            messageUntil = Time.unscaledTime + seconds;
        }

        void ChooseNetwork(int i)
        {
            var n = NetworkAt(i);
            if (n == null) return;
            if (n.kurumsal) { Say("“" + n.ssid + "” kurumsal bir ağ (eduroam gibi): robot bu ağlara bağlanamaz."); return; }
            if (n.ssid == panel.Status?.ag) { Say("Robot zaten “" + n.ssid + "” ağında."); return; }
            chosenSsid = n.ssid;
            chosenNeedsPassword = !n.kayitli && !string.IsNullOrEmpty(n.guvenlik) && n.guvenlik != "--";
            password = "";
            showPassword = false;
            passwordFocus = chosenNeedsPassword;
            if (chosenNeedsPassword) OpenTouchKeyboard();
            Say(chosenNeedsPassword ? "“" + n.ssid + "” şifresini yaz, BAĞLAN'a bas." :
                "“" + n.ssid + "” ağına geçmek için BAĞLAN'a bas. Telefonun da aynı ağda olmalı.");
        }

        void ConnectChosen()
        {
            if (string.IsNullOrEmpty(chosenSsid)) return;
            if (chosenNeedsPassword && password.Length > 0 && (password.Length < 8 || password.Length > 63)) { Say("Wi-Fi şifresi 8-63 karakter olmalı."); return; }
            string ssid = chosenSsid, pw = password;
            chosenSsid = null;
            passwordFocus = false;
            password = "";
            StartCoroutine(panel.Post(drive.Link.Host, "/api/baglan", RobotPanelClient.Json("ssid", ssid, "sifre", pw),
                (ok, e) => Say(ok ? "Robot “" + ssid + "” ağına geçiyor. Telefonu da bu ağa bağla; uygulama robotu kendisi bulur. " +
                                    "Kimse 5 dk içinde ulaşamazsa robot eski ağına döner; bağlanamazsa kendi ağını açar." : e, 30f)));
        }

        string PasswordShown()
        {
            if (!chosenNeedsPassword) return "şifre gerekmiyor";
            string shown = showPassword ? password : new string('•', password.Length);
            bool caret = passwordFocus && (int)(Time.unscaledTime * 2f) % 2 == 0;
            return shown.Length == 0 && !passwordFocus ? "şifre" : shown + (caret ? "|" : "");
        }

        void FocusPassword()
        {
            if (!chosenNeedsPassword) return;
            passwordFocus = true;
            OpenTouchKeyboard();
        }

        void OpenTouchKeyboard()
        {
            if (!TouchScreenKeyboard.isSupported) return;
            touchKeyboard = TouchScreenKeyboard.Open(password, TouchScreenKeyboardType.Default, false, false, true, false, "Wi-Fi şifresi");
        }

        void OnTextInput(char c)
        {
            if (!passwordFocus || !drive.RobotPanelOpen || char.IsControl(c) || password.Length >= 63) return;
            password += c;
        }

        void UpdateRobotPanel()
        {
            if (!textHooked && Keyboard.current != null) { Keyboard.current.onTextInput += OnTextInput; textHooked = true; }
            var link = drive.Link;
            // bağlantı (yeniden) kurulunca bir kez sor: robot yeni ağa geçtiyse bu istek geçişi onaylar
            bool connected = link.IsConnected && !link.Demo;
            if (connected && !wasConnected) StartCoroutine(panel.Refresh(link.Host));
            wasConnected = connected;
            if (robotRoot == null) return;

            bool open = drive.RobotPanelOpen;
            drive.TypingText = open && passwordFocus;
            if (!open) { passwordFocus = false; return; }
            if (connected && Time.unscaledTime - lastPanelPoll > 2f) { lastPanelPoll = Time.unscaledTime; StartCoroutine(panel.Refresh(link.Host)); }

            // yazı: bilgisayarda klavye (onTextInput), telefonda ekran klavyesi
            var kb = Keyboard.current;
            if (passwordFocus && kb != null)
            {
                if (kb.backspaceKey.wasPressedThisFrame && password.Length > 0) password = password.Substring(0, password.Length - 1);
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) ConnectChosen();
                if (kb.escapeKey.wasPressedThisFrame) passwordFocus = false;
            }
            if (touchKeyboard != null)
            {
                if (passwordFocus) password = touchKeyboard.text ?? "";
                if (touchKeyboard.status == TouchScreenKeyboard.Status.Done) { touchKeyboard = null; passwordFocus = false; }
                else if (touchKeyboard.status != TouchScreenKeyboard.Status.Visible) touchKeyboard = null;
            }

            var st = panel.Status;
            var robot = link.Status;
            if (link.Demo) robotInfo.text = "Demo: robot paneli robota bağlıyken çalışır.";
            else if (!connected) robotInfo.text = "Robota bağlı değil.";
            else if (st == null) robotInfo.text = string.IsNullOrEmpty(panel.Error) ? "Robot panelinden durum alınıyor…" : panel.Error;
            else
            {
                string band = st.frekans <= 0 ? "" : st.frekans < 3000 ? "  ·  2,4 GHz" : "  ·  5 GHz";
                string signal = robot != null && robot.sinyal != 0 ? $"  ·  {robot.sinyal} dBm ({SignalWord(robot.sinyal)})" : "";
                string mode = st.mod == "kurulum" ? "kurulum ağı açık (“" + st.kurulum_agi + "”)" :
                              st.mod == "baglaniyor" ? "“" + st.son_deneme + "” ağına bağlanıyor…" : "“" + (st.ag ?? "-") + "”";
                robotInfo.text = $"ağ {mode}{band}{signal}\n" +
                                 $"adres {st.ip ?? link.Host}  ·  internet {(st.internet == "var" ? "var" : st.internet == "portal" ? "giriş sayfası istiyor" : "yok")}" +
                                 (string.IsNullOrEmpty(st.son_hata) ? "" : "\n" + st.son_hata);
            }

            int count = st?.aglar?.Length ?? 0;
            for (int i = 0; i < NetRows; i++)
            {
                bool show = i < count;
                netButtons[i].Rt.gameObject.SetActive(show);
                netTags[i].gameObject.SetActive(show);
                if (!show) continue;
                var n = st.aglar[i];
                string tags = n.ssid == st.ag ? "bağlı" : n.kurumsal ? "kurumsal" : n.kayitli ? "kayıtlı" :
                              string.IsNullOrEmpty(n.guvenlik) || n.guvenlik == "--" ? "açık" : "şifreli";
                netTags[i].text = tags + "   " + PercentBars(n.sinyal) + " %" + n.sinyal;
            }
            if (count == 0 && st != null && connected && string.IsNullOrEmpty(robotMessage.text))
                Say("Ağ listesi boş: YENİLE'ye bas.", 3f);

            passwordRow.gameObject.SetActive(!string.IsNullOrEmpty(chosenSsid));
            if (!string.IsNullOrEmpty(chosenSsid)) passwordLabel.text = "“" + chosenSsid + "”";
            if (Time.unscaledTime > messageUntil) robotMessage.text = "";
        }

        static string SignalWord(int dbm) => dbm >= -60 ? "iyi" : dbm >= -70 ? "orta" : dbm >= -78 ? "zayıf" : "çok zayıf";

        static string PercentBars(int percent)
        {
            int n = Mathf.Clamp(Mathf.CeilToInt(percent / 25f), 0, 4);
            return new string('|', n) + new string('.', 4 - n);
        }

        void OnDisable()
        {
            if (textHooked && Keyboard.current != null) Keyboard.current.onTextInput -= OnTextInput;
            textHooked = false;
            if (drive != null) drive.TypingText = false;
        }
    }
}
