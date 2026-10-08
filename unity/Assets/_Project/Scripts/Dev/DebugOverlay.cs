using ARaki.Robot;
using UnityEngine;

namespace ARaki.Dev
{
    /// <summary>Geliştirici paneli (oyuncu arayüzü değildir): motorlara giden komut ve robotun durumu.</summary>
    public class DebugOverlay : MonoBehaviour
    {
        [SerializeField] MockRobotLink robot;

        GUIStyle label, header;
        Texture2D white;

        void OnGUI()
        {
            if (robot == null) return;
            EnsureStyles();

            var t = robot.Telemetry;
            var cmd = robot.LastCommand;
            float scale = Screen.height / 720f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            var box = new Rect(16, 16, 250, 230);
            DrawRect(box, new Color(0f, 0f, 0f, 0.55f));
            GUILayout.BeginArea(new Rect(box.x + 12, box.y + 10, box.width - 24, box.height - 20));
            GUILayout.Label("ROBOT (SİMÜLASYON)", header);
            Bar("Sol motor PWM", cmd.Left * robot.PwmLimit);
            Bar("Sağ motor PWM", cmd.Right * robot.PwmLimit);
            Bar("PWM limiti  [ ve ]", robot.PwmLimit, false);
            Bar("Batarya", t.Battery01, false);
            GUILayout.Label($"Hız {t.ForwardSpeed * 3.6f:0.0} km/s    Yön {t.YawDegrees:0}°", label);
            if (robot.FailsafeActive) GUILayout.Label("Komut yok: motorlar durdu", label);
            GUILayout.EndArea();
        }

        void Bar(string text, float value, bool signed = true)
        {
            GUILayout.Label($"{text}  {value * 100f:0}%", label);
            Rect r = GUILayoutUtility.GetRect(226, 6);
            DrawRect(r, new Color(1f, 1f, 1f, 0.12f));
            if (signed)
            {
                float mid = r.x + r.width * 0.5f;
                float w = Mathf.Abs(value) * r.width * 0.5f;
                DrawRect(new Rect(value >= 0 ? mid : mid - w, r.y, w, r.height), new Color(0.44f, 0.89f, 1f, 1f));
            }
            else
            {
                DrawRect(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value), r.height), new Color(0.44f, 0.89f, 1f, 1f));
            }
            GUILayout.Space(4);
        }

        void DrawRect(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = prev;
        }

        void EnsureStyles()
        {
            if (label != null) return;
            white = Texture2D.whiteTexture;
            label = new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = new Color(0.85f, 0.93f, 1f) } };
            header = new GUIStyle(label) { fontSize = 11, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.44f, 0.89f, 1f) } };
        }
    }
}
