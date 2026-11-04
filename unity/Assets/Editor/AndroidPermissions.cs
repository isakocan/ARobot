using System.IO;
using UnityEditor.Android;

namespace ARaki.EditorTools
{
    /// <summary>
    /// Android derlemesine Unity'nin kendiliğinden eklemediği izinleri ekler: telefonun Wi-Fi sinyalini okumak için
    /// ağ durumu (PhoneWifi). İkisi de "normal" izin, kullanıcıya sorulmaz.
    /// </summary>
    public class AndroidPermissions : IPostGenerateGradleAndroidProject
    {
        static readonly string[] Permissions = { "android.permission.ACCESS_NETWORK_STATE", "android.permission.ACCESS_WIFI_STATE" };

        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest)) return;
            string text = File.ReadAllText(manifest);
            foreach (var p in Permissions)
                if (!text.Contains("\"" + p + "\""))
                    text = text.Replace("<application", "<uses-permission android:name=\"" + p + "\" />\n  <application");
            File.WriteAllText(manifest, text);
        }
    }
}
