using System.IO;
using UnityEditor.Android;

namespace ARaki.EditorTools
{
    /// <summary>
    /// Android derlemesine Unity'nin kendiliğinden eklemediği izinleri ekler: telefonun Wi-Fi sinyalini okumak için
    /// ağ durumu (PhoneWifi). İkisi de "normal" izin, kullanıcıya sorulmaz. Ayrıca galeri uygulamalarını görebilmek için
    /// &lt;queries&gt; (Android 11+ başka uygulamaları ancak adıyla bildirilirse görür; Gallery.OpenFolder).
    /// </summary>
    public class AndroidPermissions : IPostGenerateGradleAndroidProject
    {
        static readonly string[] Permissions = { "android.permission.ACCESS_NETWORK_STATE", "android.permission.ACCESS_WIFI_STATE" };

        static readonly string[] Galleries = { "com.sec.android.gallery3d", "com.miui.gallery", "com.android.gallery3d",
                                               "com.huawei.photos", "com.coloros.gallery3d", "com.oneplus.gallery" };

        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest)) return;
            string text = File.ReadAllText(manifest);
            foreach (var p in Permissions)
                if (!text.Contains("\"" + p + "\""))
                    text = text.Replace("<application", "<uses-permission android:name=\"" + p + "\" />\n  <application");
            if (!text.Contains("<queries>"))
            {
                var q = new System.Text.StringBuilder("<queries>\n");
                foreach (var g in Galleries) q.Append("    <package android:name=\"").Append(g).Append("\" />\n");
                text = text.Replace("<application", q.Append("  </queries>\n  <application").ToString());
            }
            File.WriteAllText(manifest, text);
        }
    }
}
