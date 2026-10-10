using UnityEngine;
using UnityEngine.InputSystem;

namespace ARaki.Dev
{
    /// <summary>C tuşuyla robot kamerası (oyundaki asıl görüntü) ile üstten görünüm arasında geçiş.</summary>
    public class CameraViewSwitcher : MonoBehaviour
    {
        [SerializeField] Camera robotCamera;
        [SerializeField] Camera topCamera;
        [SerializeField] bool startWithRobotCamera = true;

        bool robotView;

        void Start() => Apply(startWithRobotCamera);

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.cKey.wasPressedThisFrame) Apply(!robotView);
        }

        void Apply(bool robot)
        {
            robotView = robot;
            robotCamera.enabled = robot;
            topCamera.enabled = !robot;
            robotCamera.tag = robot ? "MainCamera" : "Untagged";
            topCamera.tag = robot ? "Untagged" : "MainCamera";
        }
    }
}
