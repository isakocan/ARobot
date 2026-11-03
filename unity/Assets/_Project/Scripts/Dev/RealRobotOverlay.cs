using ARaki.Robot;
using ARaki.Video;
using UnityEngine;

namespace ARaki.Dev
{
    /// <summary>
    /// Gerçek robot sahnesinin geliştirici göstergesi (oyuncu arayüzü değildir): bağlantı, gecikme,
    /// Wi-Fi sinyali, görüntü hızı, motor akımları. Bağlı değilken robot adresi buradan girilir.
    /// </summary>
    public class RealRobotOverlay : MonoBehaviour
    {
        [SerializeField] RealRobotLink robot;
        [SerializeField] MjpegStream video;

        string address;
        GUIStyle label, dim, field, button;
        Texture2D white;

        void OnGUI()
        {
            if (robot == null) return;
            EnsureStyles();
            float scale = Screen.height / 720f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            var box = new Rect(16, 16, 300, robot.IsConnected ? 132 : 112);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(box, white);
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(box.x + 12, box.y + 10, box.width - 24, box.height - 20));

            if (!robot.IsConnected)
            {
                GUILayout.Label("Robota bağlanılıyor: " + robot.Host, label);
                if (!string.IsNullOrEmpty(robot.LastError)) GUILayout.Label(robot.LastError, dim);
                GUILayout.BeginHorizontal();
                address ??= robot.Host;
                address = GUILayout.TextField(address, field, GUILayout.Width(190));
                if (GUILayout.Button("Bağlan", button)) robot.Connect(address);
                GUILayout.EndHorizontal();
            }
            else
            {
                var s = robot.Status;
                string link = robot.IsWeak ? "ZAYIF" : $"{robot.RoundTripMs:0} ms";
                GUILayout.Label($"{robot.Host}   {link}   {(s != null && s.sinyal != 0 ? s.sinyal + " dBm" : "")}", label);
                GUILayout.Label($"Görüntü {(video != null ? video.Fps : 0f):0} fps   kamera {s?.kamera ?? "?"}   " +
                                $"{(s != null ? s.veri_kbps / 1000f : 0f):0.0} Mbit/s", dim);
                GUILayout.Label(s == null || !s.arduino ? "Arduino yanıt vermiyor"
                    : $"Motor sol {s.motor.sol} ({s.motor.sol_ma} mA)  sağ {s.motor.sag} ({s.motor.sag_ma} mA)", dim);
                GUILayout.Label($"Güç sınırı %{robot.PwmLimit * 100f:0}  (R / F)   L ışık  H korna  X dörtlü", dim);
                if (s != null && s.motor != null && s.motor.ariza) GUILayout.Label("Motor sürücü arıza bildiriyor", label);
            }
            GUILayout.EndArea();
        }

        void EnsureStyles()
        {
            if (label != null) return;
            white = Texture2D.whiteTexture;
            label = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            label.normal.textColor = Color.white;
            dim = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            dim.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
            field = new GUIStyle(GUI.skin.textField) { fontSize = 14 };
            button = new GUIStyle(GUI.skin.button) { fontSize = 14 };
        }
    }
}
