using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace ArTarama
{
    /// <summary>
    /// Uygulamanın günlüğü (23 Kasım, kullanıcı: sağlam loglama). Unity'nin bütün log satırları (Debug.Log, uyarı, hata,
    /// yakalanmamış istisna) ve Gunluk.Yaz ile yazılan olaylar zaman damgasıyla dosyaya gider:
    /// files/gunluk/yyyyMMdd_HHmmss.txt (uygulama her açılışta yeni dosya; en çok 20 dosya saklanır).
    /// Bilgisayara: adb pull /sdcard/Android/data/com.ekip.artarama/files/gunluk
    /// Her iş parçacığından çağrılabilir; yazma kuyrukla, saniyede bir diske.
    /// </summary>
    public static class Gunluk
    {
        static readonly object kilit = new object();
        static StringBuilder bekleyen = new StringBuilder();
        static string dosya;
        static float sonYazma;
        static bool kurulu;
        public static string Klasor => Path.Combine(Application.persistentDataPath, "gunluk");
        public static string Dosya => dosya;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Kur()
        {
            if (kurulu) return;
            kurulu = true;
            try
            {
                Directory.CreateDirectory(Klasor);
                var eski = Directory.GetFiles(Klasor, "*.txt");
                Array.Sort(eski);
                for (int i = 0; i < eski.Length - 19; i++) File.Delete(eski[i]);
                dosya = Path.Combine(Klasor, DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            }
            catch (Exception) { dosya = null; }
            Application.logMessageReceivedThreaded += (m, st, tur) =>
            {
                string on = tur == LogType.Log ? "" : tur == LogType.Warning ? "UYARI " : "HATA ";
                Ekle(on + m + (tur == LogType.Exception || tur == LogType.Error ? "\n    " + st.Replace("\n", "\n    ") : ""));
            };
            Ekle("acildi: " + Application.version + " " + SystemInfo.deviceModel + " " + SystemInfo.operatingSystem);
            var go = new GameObject("Gunluk") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Yazici>();
        }

        /// <summary>Olay satırı (etiket: kısa alan adı, ör. "tarama", "dev", "kalem").</summary>
        public static void Yaz(string etiket, string metin) => Ekle("[" + etiket + "] " + metin);

        static void Ekle(string satir)
        {
            lock (kilit) bekleyen.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(' ').Append(satir).Append('\n');
        }

        public static void Bosalt()
        {
            string s;
            lock (kilit) { if (bekleyen.Length == 0) return; s = bekleyen.ToString(); bekleyen.Clear(); }
            try { if (dosya != null) File.AppendAllText(dosya, s, new UTF8Encoding(false)); } catch (Exception) { }
        }

        class Yazici : MonoBehaviour
        {
            void Update() { if (Time.unscaledTime - sonYazma > 1f) { sonYazma = Time.unscaledTime; Bosalt(); } }
            void OnApplicationPause(bool p) { if (p) Bosalt(); }
            void OnApplicationQuit() { Ekle("kapandi"); Bosalt(); }
        }
    }
}
