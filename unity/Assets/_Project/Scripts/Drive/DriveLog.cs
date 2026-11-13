using System;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace ARaki.Drive
{
    /// <summary>
    /// Sürüş kaydı: Unity'nin tüm log iletilerini zaman damgasıyla persistentDataPath/surus.log dosyasına yazar
    /// (2 MB'ı aşınca surus.log.1'e döner). Bağlantı koptuktan sonra ne olduğunu okuyabilmek için.
    /// Kendiliğinden başlar; sahnede bir şey eklemek gerekmez.
    /// </summary>
    public static class DriveLog
    {
        const long MaxBytes = 2 * 1024 * 1024;
        static readonly object gate = new object();
        static readonly StringBuilder buffer = new StringBuilder();
        static string path;
        static Timer flushTimer;
        static bool started;

        public static string Path => path;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Start()
        {
            lock (gate)
            {
                if (started) return;
                started = true;
                try { path = System.IO.Path.Combine(Application.persistentDataPath, "surus.log"); }
                catch (Exception) { return; }
            }
            Application.logMessageReceivedThreaded -= OnLog;
            Application.logMessageReceivedThreaded += OnLog;
            Application.quitting -= Flush;
            Application.quitting += Flush;
            flushTimer = new Timer(_ => Flush(), null, 1000, 1000);
            Write("---- oturum başladı " + DateTime.Now.ToString("yyyy-MM-dd") + " ----");
        }

        static void OnLog(string message, string stack, LogType type)
        {
            string tag = type == LogType.Log ? "" : type == LogType.Warning ? "UYARI " : type == LogType.Assert ? "ASSERT " : type == LogType.Exception ? "ISTISNA " : "HATA ";
            Write(tag + message);
            if (type == LogType.Exception || type == LogType.Error) Write(stack);
        }

        static void Write(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + text.TrimEnd();
            lock (gate) buffer.Append(line).Append('\n');
        }

        static void Flush()
        {
            lock (gate)
            {
                if (path == null || buffer.Length == 0) return;
                try
                {
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        string old = path + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(path, old);
                    }
                    File.AppendAllText(path, buffer.ToString(), Encoding.UTF8);
                }
                catch (Exception) { }   // kayıt yazılamasa da oyun etkilenmesin
                buffer.Length = 0;
            }
        }
    }
}
