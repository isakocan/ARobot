using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ArTarama
{
    /// <summary>
    /// Ev haritasini robota gonderir: robotun koprusu (HTTP 8080) POST /api/ev ile {"ev": ev.json, "zemin": zemin.json}
    /// alir, saklar ve konum bulmada isaretleri kullanir; robot uygulamasi kucuk haritayi oradan ceker. Iki uygulama ayni
    /// telefonda olsa da birbirinin dosyasini okuyamaz (Android), harita robot uzerinden tasinir.
    /// Robotu bulma: son adres, jetson.local, UDP yayini ("bul"), son olarak alt ag taramasi (EDUROAM /22 dahil).
    /// </summary>
    public static class RobotaGonder
    {
        const string AdresAnahtari = "robot_adresi";
        const int UdpPort = 8081, HttpPort = 8080;

        public static async Task<string> Gonder(string evKlasoru, Action<string> durum)
        {
            string ev = File.ReadAllText(Path.Combine(evKlasoru, "ev.json"));
            string zp = Path.Combine(evKlasoru, "zemin.json");
            string zemin = File.Exists(zp) ? File.ReadAllText(zp) : "null";
            var govde = Encoding.UTF8.GetBytes("{\"ev\":" + ev + ",\"zemin\":" + zemin + "}");
            string son = PlayerPrefs.GetString(AdresAnahtari, "");
            string ip = await Task.Run(() => Bul(son, durum));
            if (ip == null) return "Robot bulunamadı. Robot açık ve telefonla aynı ağda mı?";
            PlayerPrefs.SetString(AdresAnahtari, ip); PlayerPrefs.Save();
            durum?.Invoke("Gönderiliyor");
            return await Task.Run(() =>
            {
                try
                {
                    var istek = (HttpWebRequest)WebRequest.Create("http://" + ip + ":" + HttpPort + "/api/ev");
                    istek.Method = "POST"; istek.ContentType = "application/json"; istek.Timeout = 8000; istek.ContentLength = govde.Length;
                    using (var s = istek.GetRequestStream()) s.Write(govde, 0, govde.Length);
                    using (var c = (HttpWebResponse)istek.GetResponse())
                        return c.StatusCode == HttpStatusCode.OK ? null : "Robot kabul etmedi (" + (int)c.StatusCode + ")";
                }
                catch (Exception e) { Debug.LogWarning("[ArTarama] robota gonderilemedi: " + e.Message); return "Gönderilemedi: " + e.Message; }
            });
        }

        /// <summary>Robotun adresini bulur (son adres, jetson.local, UDP yayını, alt ağ taraması); bulunamazsa null.</summary>
        public static async Task<string> RobotuBul(Action<string> durum)
        {
            string son = PlayerPrefs.GetString(AdresAnahtari, "");
            string ip = await Task.Run(() => Bul(son, durum));
            if (ip != null) { PlayerPrefs.SetString(AdresAnahtari, ip); PlayerPrefs.Save(); }
            return ip;
        }

        static string Bul(string son, Action<string> durum)
        {
            if (!string.IsNullOrEmpty(son) && Cevap(son)) return son;
            try { foreach (var a in Dns.GetHostAddresses("jetson.local")) if (a.AddressFamily == AddressFamily.InterNetwork && Cevap(a.ToString())) return a.ToString(); }
            catch (Exception) { }
            durum?.Invoke("Robot aranıyor");
            string y = Yayin(); if (y != null) return y;
            foreach (var (adres, onek) in YerelAdresler())
            {
                var b = adres.GetAddressBytes(); uint ben = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
                int on = Math.Max(22, Math.Min(30, onek)); uint maske = 0xFFFFFFFFu << (32 - on), ag = ben & maske, n = ~maske;
                var isler = new List<Task<string>>();
                for (uint i = 1; i < n; i++)
                {
                    uint a = ag | i; if (a == ben) continue;
                    string ip = (a >> 24) + "." + ((a >> 16) & 255) + "." + ((a >> 8) & 255) + "." + (a & 255);
                    isler.Add(PortAcik(ip, HttpPort, 900).ContinueWith(t => t.Result ? ip : null));
                }
                foreach (var ip in Task.WhenAll(isler).Result) if (ip != null && Cevap(ip)) return ip;
            }
            return null;
        }

        static bool Cevap(string host)
        {
            try
            {
                using (var u = new UdpClient(AddressFamily.InterNetwork))
                {
                    u.Client.ReceiveTimeout = 400; var soru = Encoding.ASCII.GetBytes("{\"t\":\"bul\"}");
                    for (int i = 0; i < 3; i++)
                    {
                        u.Send(soru, soru.Length, host, UdpPort);
                        try { var uc = new IPEndPoint(IPAddress.Any, 0); if (Encoding.UTF8.GetString(u.Receive(ref uc)).Contains("\"robot\"")) return true; }
                        catch (SocketException) { }
                    }
                }
            }
            catch (Exception) { }
            return false;
        }

        static string Yayin()
        {
            try
            {
                using (var u = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true })
                {
                    u.Client.ReceiveTimeout = 500; var soru = Encoding.ASCII.GetBytes("{\"t\":\"bul\"}");
                    for (int i = 0; i < 3; i++)
                    {
                        u.Send(soru, soru.Length, new IPEndPoint(IPAddress.Broadcast, UdpPort));
                        try { var uc = new IPEndPoint(IPAddress.Any, 0); if (Encoding.UTF8.GetString(u.Receive(ref uc)).Contains("\"robot\"")) return uc.Address.ToString(); }
                        catch (SocketException) { }
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        static async Task<bool> PortAcik(string ip, int port, int ms)
        {
            using (var c = new TcpClient())
            {
                try { var t = c.ConnectAsync(ip, port); return await Task.WhenAny(t, Task.Delay(ms)) == t && c.Connected; }
                catch (Exception) { return false; }
            }
        }

        static IEnumerable<(IPAddress, int)> YerelAdresler()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var b = ua.Address.GetAddressBytes(); if (b[0] == 169 && b[1] == 254) continue;
                    int on = 22; try { on = ua.PrefixLength; } catch (Exception) { }   // bilinmiyorsa /22 (EDUROAM)
                    yield return (ua.Address, on);
                }
            }
        }
    }
}
