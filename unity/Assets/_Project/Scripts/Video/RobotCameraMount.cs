using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace ARaki.Video
{
    /// <summary>
    /// 360° kameranın robota takılış açısını (yön / eğim / yatma) DualFisheye malzemesine verir.
    /// Açılar sürüş sayfasının ayarlar panelinde ayarlanıp köprüde saklanır (/api/kamera-ayari);
    /// burada oradan okunur, böylece iki uygulama aynı kalibrasyonu kullanır.
    /// </summary>
    public class RobotCameraMount : MonoBehaviour
    {
        [System.Serializable]
        class Saved { public float yon = 0f, egim = 0f, yatma = 90f; }

        [SerializeField] ARaki.Robot.RealRobotLink robot;
        [SerializeField] Renderer sphere;
        [SerializeField] float yaw;          // yon
        [SerializeField] float pitch;        // egim
        [SerializeField] float roll = 90f;   // yatma (kamera robota yan yatırılmış takılı)

        // 2. lensin 1. lense göre dönmesi (derece, x/y/z); karelerden ölçüldü
        static readonly Vector3 Lens2Rotation = new Vector3(-0.141f, 0.984f, 0.683f);

        string loadedFrom;

        void Start() => Apply();

        void Update()
        {
            if (robot != null && !robot.Demo && robot.IsConnected && robot.Host != loadedFrom)
            {
                loadedFrom = robot.Host;
                StartCoroutine(Load(robot.Host));
            }
        }

        IEnumerator Load(string host)
        {
            using (var req = UnityWebRequest.Get("http://" + host + ":" + ARaki.Net.UdpRobotConnection.HttpPort + "/api/kamera-ayari"))
            {
                req.timeout = 4;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success || req.downloadHandler.text.Length < 3) yield break;
                var s = JsonUtility.FromJson<Saved>(req.downloadHandler.text);
                yaw = s.yon; pitch = s.egim; roll = s.yatma;
                Apply();
            }
        }

        public void Apply()
        {
            if (sphere == null) return;
            var mat = sphere.sharedMaterial;
            mat.SetMatrix("_Mount", Mul(Mul(Rz(roll), Rx(pitch)), Ry(yaw)));
            mat.SetMatrix("_Lens2Rotation", Mul(Mul(Rz(Lens2Rotation.z), Ry(Lens2Rotation.y)), Rx(Lens2Rotation.x)));
        }

        // Sürüş sayfasındaki satır düzeni döndürme matrisleriyle birebir aynı (açılar derece)
        static Matrix4x4 Rx(float a)
        {
            float c = Mathf.Cos(a * Mathf.Deg2Rad), s = Mathf.Sin(a * Mathf.Deg2Rad);
            return Rows(1, 0, 0, 0, c, -s, 0, s, c);
        }

        static Matrix4x4 Ry(float a)
        {
            float c = Mathf.Cos(a * Mathf.Deg2Rad), s = Mathf.Sin(a * Mathf.Deg2Rad);
            return Rows(c, 0, s, 0, 1, 0, -s, 0, c);
        }

        static Matrix4x4 Rz(float a)
        {
            float c = Mathf.Cos(a * Mathf.Deg2Rad), s = Mathf.Sin(a * Mathf.Deg2Rad);
            return Rows(c, -s, 0, s, c, 0, 0, 0, 1);
        }

        static Matrix4x4 Rows(float a, float b, float c, float d, float e, float f, float g, float h, float i)
        {
            var m = Matrix4x4.identity;
            m.SetRow(0, new Vector4(a, b, c, 0));
            m.SetRow(1, new Vector4(d, e, f, 0));
            m.SetRow(2, new Vector4(g, h, i, 0));
            return m;
        }

        static Matrix4x4 Mul(Matrix4x4 a, Matrix4x4 b) => a * b;
    }
}
