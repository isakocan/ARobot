using UnityEngine;

namespace ARaki.Net
{
    /// <summary>
    /// Telefonun bağlı olduğu Wi-Fi'ın sinyal gücü (dBm). Robotun kendi ağına bağlanınca robot tarafında ölçülecek
    /// istemci sinyali olmaz; o zaman bağlantının kalitesini bu gösterir. Yalnız Android'de; ek izin istemez
    /// (ConnectivityManager → NetworkCapabilities.getSignalStrength, Android 10+). 2 sn'de bir okunur.
    /// </summary>
    public static class PhoneWifi
    {
        public static int? Rssi { get; private set; }
        static float next;

        public static void Poll()
        {
            if (Application.platform != RuntimePlatform.Android || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 2f;
#if UNITY_ANDROID
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var cm = activity.Call<AndroidJavaObject>("getSystemService", "connectivity"))
                {
                    int? best = null;
                    foreach (var net in cm.Call<AndroidJavaObject[]>("getAllNetworks"))
                    {
                        using (net)
                        using (var caps = cm.Call<AndroidJavaObject>("getNetworkCapabilities", net))
                        {
                            if (caps == null || !caps.Call<bool>("hasTransport", 1)) continue;   // 1 = TRANSPORT_WIFI
                            int s = caps.Call<int>("getSignalStrength");
                            if (s > -127 && s < 0) best = s;
                        }
                    }
                    Rssi = best;
                }
            }
            catch (System.Exception) { Rssi = null; }
#endif
        }
    }
}
