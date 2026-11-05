using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ARaki.Drive
{
    /// <summary>
    /// Kayıtlar paneli (menü › KAYITLAR): son fotoğraf ve videolar, seçilen fotoğrafın önizlemesi, kayıt klasörünü
    /// (bilgisayar) ya da galeriyi (telefon) açma. Telefonda her kayıt ayrıca galeriye kopyalanır (Gallery).
    /// </summary>
    public partial class DriveCockpit
    {
        const int RecordRows = 9;
        RectTransform recordsRoot;
        readonly Btn[] recordButtons = new Btn[RecordRows];
        readonly Text[] recordTags = new Text[RecordRows];
        FileInfo[] recordFiles = new FileInfo[0];
        string selectedRecord;
        RawImage previewImage;
        Texture2D previewTexture;
        Text previewText, recordsWhere;
        float recordsListedAt = -100f;

        void BuildRecordsPanel()
        {
            var r = new Rect(W / 2f - 390f, H - 100f - 420f, 780f, 420f);
            recordsRoot = Panel("Kayıtlar", r);
            const float L = 20f;
            float y = r.height - 46f;
            Label(recordsRoot, "KAYITLAR", font, 18, Text, TextAnchor.MiddleLeft, V(L + 150f, y + 14f), V(300, 24));
            y -= 34f;
            recordsWhere = Label(recordsRoot, "", mono, 12, Dim, TextAnchor.MiddleLeft, V(L + 370f, y + 8f), V(740, 16));
            y -= 34f;
            float listW = 330f;
            float top = y;
            for (int i = 0; i < RecordRows; i++)
            {
                int k = i;
                recordButtons[i] = Button("", new Rect(r.x + L, r.y + y, listW, 27f), () => ChooseRecord(k), null,
                                          () => k < recordFiles.Length && recordFiles[k].FullName == selectedRecord, 14, TextAnchor.MiddleLeft,
                                          recordsRoot, () => k < recordFiles.Length ? RecordTitle(recordFiles[k]) : "");
                recordTags[i] = Label(recordsRoot, "", mono, 11, Dim, TextAnchor.MiddleRight, V(L + listW - 70f - 10f, y + 13.5f), V(140, 14));
                y -= 30f;
            }
            // önizleme: sağda 16:9 çerçeve
            float px = L + listW + 20f, pw = r.width - px - L, ph = pw * 9f / 16f, py = top + 27f - ph;
            var frame = Shape("Önizleme çerçevesi", recordsRoot);
            frame.RoundRect(new Rect(px - 1f, py - 1f, pw + 2f, ph + 2f), 8f, new Color(1f, 1f, 1f, 0.12f));
            frame.RoundRect(new Rect(px, py, pw, ph), 7f, Hex("#050607"));
            previewImage = new GameObject("Önizleme", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            var prt = (RectTransform)previewImage.transform;
            prt.SetParent(recordsRoot, false);
            prt.anchorMin = prt.anchorMax = Vector2.zero;
            prt.pivot = Vector2.zero;
            prt.anchoredPosition = V(px + 2f, py + 2f);
            prt.sizeDelta = V(pw - 4f, ph - 4f);
            previewImage.raycastTarget = false;
            previewImage.enabled = false;
            previewText = Label(recordsRoot, "", font, 14, Dim, TextAnchor.MiddleCenter, V(px + pw / 2f, py + ph / 2f), V(pw - 20f, 40));
            previewText.horizontalOverflow = HorizontalWrapMode.Wrap;
            float by = py - 40f;
            Button(Application.isMobilePlatform ? "GALERİYİ AÇ" : "KLASÖRÜ AÇ", new Rect(r.x + px, r.y + by, 140f, 30f), Gallery.OpenFolder,
                   null, null, 13, TextAnchor.MiddleCenter, recordsRoot);
            if (!Application.isMobilePlatform)
                Button("DOSYAYI AÇ", new Rect(r.x + px + 150f, r.y + by, 130f, 30f),
                       () => { if (selectedRecord != null && File.Exists(selectedRecord)) Gallery.OpenFile(selectedRecord); },
                       null, null, 13, TextAnchor.MiddleCenter, recordsRoot);
        }

        static string RecordTitle(FileInfo f)
        {
            bool video = f.Extension.Equals(".avi", StringComparison.OrdinalIgnoreCase);
            var t = f.LastWriteTime;
            return (video ? "Video  " : "Foto   ") + t.Day + " " + Months[t.Month - 1].ToLowerInvariant() + "  " + t.ToString("HH:mm:ss");
        }

        void ListRecords()
        {
            recordsListedAt = Time.unscaledTime;
            try
            {
                recordFiles = new DirectoryInfo(ScreenRecorder.Folder).GetFiles()
                    .Where(f => f.Extension == ".png" || f.Extension == ".avi")
                    .OrderByDescending(f => f.LastWriteTimeUtc).Take(RecordRows).ToArray();
            }
            catch (Exception) { recordFiles = new FileInfo[0]; }
        }

        void ChooseRecord(int i)
        {
            if (i >= recordFiles.Length) return;
            var f = recordFiles[i];
            selectedRecord = f.FullName;
            if (f.Extension == ".png")
            {
                if (previewTexture == null) previewTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                try
                {
                    if (previewTexture.LoadImage(File.ReadAllBytes(f.FullName), false))
                    {
                        previewImage.texture = previewTexture;
                        previewImage.enabled = true;
                        previewText.text = "";
                        return;
                    }
                }
                catch (Exception) { }
            }
            previewImage.enabled = false;
            previewText.text = f.Extension == ".avi"
                ? (Application.isMobilePlatform ? "Video galeride: Movies › " + Gallery.Album : "Video: DOSYAYI AÇ ile oynatılır") +
                  $"\n{f.Length / 1048576f:0.0} MB"
                : "Önizleme açılamadı";
        }

        void UpdateRecordsPanel()
        {
            if (recordsRoot == null) return;
            recordsRoot.gameObject.SetActive(drive.RecordsOpen);
            if (!drive.RecordsOpen) return;
            if (Time.unscaledTime - recordsListedAt > 2f) ListRecords();
            recordsWhere.text = "Kayıt yeri: " + Gallery.WhereText;
            for (int i = 0; i < RecordRows; i++)
            {
                bool show = i < recordFiles.Length;
                recordButtons[i].Rt.gameObject.SetActive(show);
                recordTags[i].gameObject.SetActive(show);
                if (show) recordTags[i].text = recordFiles[i].Length >= 1048576 ? $"{recordFiles[i].Length / 1048576f:0.0} MB" : $"{recordFiles[i].Length / 1024} KB";
            }
            if (recordFiles.Length == 0) { previewImage.enabled = false; previewText.text = "Henüz kayıt yok: menüden FOTO ya da VİDEO."; }
            else if (selectedRecord == null) ChooseRecord(0);
        }
    }
}
