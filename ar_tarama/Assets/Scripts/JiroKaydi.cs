using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using Gyro = UnityEngine.InputSystem.Gyroscope;

namespace ArTarama
{
    /// <summary>
    /// Geliştirici ölçüm aracı (25 Kasım): telefon robotun üstünde düz yatarken jiroskop + yönelim (oyun dönme vektörü,
    /// pusulasız) kaydı. Robotun konum denemesinde "gerçek yön" dayanağı: telefonun kamerası kullanılmaz. Ekran açık kalmalı
    /// (Unity arka planda durur); kayıt sırasında ekran neredeyse siyah (AMOLED).
    /// Çıktı: files/olcum/jiro_&lt;zaman&gt;.csv: utc_sn, uygulama_sn, wx, wy, wz (rad/sn, cihaz), gx, gy, gz (yerçekimi),
    /// qx, qy, qz, qw (yönelim).
    /// </summary>
    public class JiroKaydi : MonoBehaviour
    {
        public bool Kayitta { get; private set; }
        public string Dosya { get; private set; }
        public int Satir { get; private set; }
        public double Baslangic { get; private set; }
        StreamWriter yaz; readonly StringBuilder sb = new StringBuilder(256);
        static readonly CultureInfo C = CultureInfo.InvariantCulture;

        public bool Baslat()
        {
            if (Kayitta) return true;
            var g = Gyro.current; var a = AttitudeSensor.current; var y = GravitySensor.current;
            if (g == null) return false;
            InputSystem.EnableDevice(g); g.samplingFrequency = 100;
            if (a != null) { InputSystem.EnableDevice(a); a.samplingFrequency = 100; }
            if (y != null) { InputSystem.EnableDevice(y); y.samplingFrequency = 100; }
            string k = Path.Combine(Application.persistentDataPath, "olcum"); Directory.CreateDirectory(k);
            Dosya = Path.Combine(k, "jiro_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
            yaz = new StreamWriter(Dosya, false, new UTF8Encoding(false));
            yaz.WriteLine("utc_sn,uygulama_sn,wx,wy,wz,gx,gy,gz,qx,qy,qz,qw");
            Kayitta = true; Satir = 0; Baslangic = Time.realtimeSinceStartupAsDouble;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Gunluk.Yaz("jiro", "kayit basladi " + Dosya + " jiro=" + g.name + " yonelim=" + (a != null ? a.name : "yok"));
            return true;
        }

        public void Durdur()
        {
            if (!Kayitta) return;
            Kayitta = false;
            try { yaz.Flush(); yaz.Dispose(); } catch (Exception) { }
            yaz = null;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            Gunluk.Yaz("jiro", "kayit bitti " + Satir + " satir");
        }

        void Update()
        {
            if (!Kayitta) return;
            var g = Gyro.current; if (g == null) return;
            Vector3 w = g.angularVelocity.ReadValue();
            Vector3 y = GravitySensor.current != null ? GravitySensor.current.gravity.ReadValue() : Vector3.zero;
            Quaternion q = AttitudeSensor.current != null ? AttitudeSensor.current.attitude.ReadValue() : Quaternion.identity;
            double utc = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            sb.Clear();
            sb.Append(utc.ToString("F4", C)).Append(',').Append(Time.realtimeSinceStartupAsDouble.ToString("F4", C)).Append(',')
              .Append(w.x.ToString("F5", C)).Append(',').Append(w.y.ToString("F5", C)).Append(',').Append(w.z.ToString("F5", C)).Append(',')
              .Append(y.x.ToString("F4", C)).Append(',').Append(y.y.ToString("F4", C)).Append(',').Append(y.z.ToString("F4", C)).Append(',')
              .Append(q.x.ToString("F5", C)).Append(',').Append(q.y.ToString("F5", C)).Append(',').Append(q.z.ToString("F5", C)).Append(',').Append(q.w.ToString("F5", C));
            yaz.WriteLine(sb.ToString());
            Satir++;
            if ((Satir & 255) == 0) yaz.Flush();
        }

        void OnApplicationPause(bool p) { if (p && Kayitta) { try { yaz.Flush(); } catch (Exception) { } } }
        void OnDestroy() { Durdur(); }
    }
}
