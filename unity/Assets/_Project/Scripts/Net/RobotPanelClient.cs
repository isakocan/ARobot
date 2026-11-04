using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ARaki.Net
{
    /// <summary>Robot panelinin (Jetson, port 80) /api/durum cevabı; yalnızca uygulamanın kullandığı alanlar.</summary>
    [Serializable]
    public class PanelStatus
    {
        [Serializable]
        public class Network
        {
            public string ssid, guvenlik;
            public int sinyal;          // % (0-100, nmcli)
            public bool kurumsal, kayitli;
        }

        public string mod;              // istemci | kurulum | baglaniyor
        public string ag;               // bağlı Wi-Fi profili (çoğunlukla ağ adı)
        public string internet;         // var | portal | yok
        public string ip, bant, kurulum_agi, son_hata, son_deneme;
        public int frekans;             // MHz (0 = bilinmiyor)
        public Network[] aglar;
    }

    /// <summary>
    /// Robot paneliyle konuşur (tarayıcıdaki panelin yaptığı işler): durum, ağ tarama, ağa bağlanma, Wi-Fi bandı,
    /// kapatma / yeniden başlatma, kurulum ağı. İstekler UnityWebRequest koroutinleri; çağıran MonoBehaviour çalıştırır.
    /// </summary>
    public class RobotPanelClient
    {
        public const int Port = 80;

        public PanelStatus Status { get; private set; }
        public float StatusAt { get; private set; } = -100f;
        public bool Busy { get; private set; }
        public bool Scanning { get; private set; }
        public string Error { get; private set; } = "";

        static string Url(string host, string path) => "http://" + host + (Port == 80 ? "" : ":" + Port) + path;

        public IEnumerator Refresh(string host)
        {
            if (string.IsNullOrEmpty(host) || Busy) yield break;
            Busy = true;
            using (var req = UnityWebRequest.Get(Url(host, "/api/durum")))
            {
                req.timeout = 4;
                yield return req.SendWebRequest();
                Busy = false;
                if (req.result != UnityWebRequest.Result.Success) { Error = "Robot paneline ulaşılamadı."; yield break; }
                try
                {
                    Status = JsonUtility.FromJson<PanelStatus>(req.downloadHandler.text);
                    StatusAt = Time.unscaledTime;
                    Error = "";
                }
                catch (Exception) { Error = "Robot panelinin cevabı okunamadı."; }
            }
        }

        /// <summary>Yakındaki ağları tarar (birkaç saniye sürer; bu sırada görüntü bir an takılabilir), sonra durumu yeniler.</summary>
        public IEnumerator Scan(string host)
        {
            if (Scanning) yield break;
            Scanning = true;
            yield return Post(host, "/api/tara", "{}", null, 25);
            Scanning = false;
            yield return Refresh(host);
        }

        /// <summary>POST; done(ok, hata metni). Panel hatayı {"hata": "..."} olarak döner.</summary>
        public IEnumerator Post(string host, string path, string json, Action<bool, string> done, int timeout = 6)
        {
            if (string.IsNullOrEmpty(host)) { done?.Invoke(false, "Robot bulunamadı."); yield break; }
            using (var req = new UnityWebRequest(Url(host, path), "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = timeout;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) { done?.Invoke(true, ""); yield break; }
                string message = "Robot paneline ulaşılamadı.";
                try
                {
                    var e = JsonUtility.FromJson<ErrorReply>(req.downloadHandler?.text ?? "");
                    if (e != null && !string.IsNullOrEmpty(e.hata)) message = e.hata;
                }
                catch (Exception) { }
                done?.Invoke(false, message);
            }
        }

        [Serializable] class ErrorReply { public string hata; }

        public static string Json(params string[] keyValues)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i + 1 < keyValues.Length; i += 2)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(keyValues[i]).Append("\":\"").Append(Escape(keyValues[i + 1])).Append('"');
            }
            return sb.Append('}').ToString();
        }

        static string Escape(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
