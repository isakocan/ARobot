using System;
using System.IO;
using UnityEngine;

namespace ARaki.Drive
{
    /// <summary>
    /// Kayıtları kullanıcının bulacağı yere koyar ve açar. Telefonda fotoğraflar Galeri › Pictures/Robot Sürüş,
    /// videolar Movies/Robot Sürüş klasörüne kopyalanır (Android MediaStore; izin gerekmez). Bilgisayarda kayıtlar
    /// zaten Resimler\Robot klasöründe; klasör ya da dosya Windows'un kendi uygulamasıyla açılır.
    /// </summary>
    public static class Gallery
    {
        public const string Album = "Robot Sürüş";

        public static string WhereText => Application.isMobilePlatform
            ? "Galeri › " + Album + " (fotoğraflar Pictures, videolar Movies içinde)"
            : ScreenRecorder.Folder;

        /// <summary>Telefonda dosyayı galeriye kopyalar; başarılıysa true. Bilgisayarda bir şey yapmaz.</summary>
        public static bool Publish(string path)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                bool video = path.EndsWith(".avi", StringComparison.OrdinalIgnoreCase);
                using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                using (var media = new AndroidJavaClass(video ? "android.provider.MediaStore$Video$Media" : "android.provider.MediaStore$Images$Media"))
                using (var collection = media.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI"))
                using (var values = new AndroidJavaObject("android.content.ContentValues"))
                {
                    values.Call("put", "_display_name", Path.GetFileName(path));
                    values.Call("put", "mime_type", video ? "video/x-msvideo" : "image/png");
                    values.Call("put", "relative_path", (video ? "Movies/" : "Pictures/") + Album);
                    using (var item = resolver.Call<AndroidJavaObject>("insert", collection, values))
                    {
                        if (item == null) return false;
                        using (var output = resolver.Call<AndroidJavaObject>("openOutputStream", item))
                        using (var input = File.OpenRead(path))
                        {
                            var chunk = new byte[1 << 20];
                            int n;
                            while ((n = input.Read(chunk, 0, chunk.Length)) > 0)
                            {
                                var part = new sbyte[n];
                                Buffer.BlockCopy(chunk, 0, part, 0, n);
                                output.Call("write", part);
                            }
                            output.Call("close");
                        }
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Galeri] kopyalanamadı: " + e.Message);
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>Kayıt klasörünü (bilgisayar) ya da galeriyi (telefon) açar.</summary>
        public static void OpenFolder()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intentClass = new AndroidJavaClass("android.content.Intent"))
                using (var intent = intentClass.CallStatic<AndroidJavaObject>("makeMainSelectorActivity",
                           "android.intent.action.MAIN", "android.intent.category.APP_GALLERY"))
                {
                    intent.Call<AndroidJavaObject>("addFlags", 0x10000000);   // FLAG_ACTIVITY_NEW_TASK
                    activity.Call("startActivity", intent);
                }
            }
            catch (Exception e) { Debug.LogWarning("[Galeri] açılamadı: " + e.Message); }
#else
            Application.OpenURL(new Uri(ScreenRecorder.Folder).AbsoluteUri);
#endif
        }

        /// <summary>Bilgisayarda dosyayı varsayılan uygulamayla açar (fotoğraf görüntüleyici, video oynatıcı).</summary>
        public static void OpenFile(string path) => Application.OpenURL(new Uri(path).AbsoluteUri);
    }
}
