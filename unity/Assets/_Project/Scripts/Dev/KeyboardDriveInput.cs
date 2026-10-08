using ARaki.Robot;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ARaki.Dev
{
    /// <summary>PC'de test için: W/S ileri-geri, A/D dönüş (ok tuşları da çalışır). Telefonda sanal joystick yerini alacak.</summary>
    [RequireComponent(typeof(MockRobotLink))]
    public class KeyboardDriveInput : MonoBehaviour
    {
        IRobotLink link;

        void Awake() => link = GetComponent<MockRobotLink>();

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            float throttle = Axis(kb.wKey.isPressed || kb.upArrowKey.isPressed, kb.sKey.isPressed || kb.downArrowKey.isPressed);
            float steer = Axis(kb.dKey.isPressed || kb.rightArrowKey.isPressed, kb.aKey.isPressed || kb.leftArrowKey.isPressed);

            // Arcade karışımı: dönüş, iki tekerleğin hızını zıt yönde kaydırır.
            float l = throttle + steer;
            float r = throttle - steer;
            float max = Mathf.Max(1f, Mathf.Abs(l), Mathf.Abs(r));
            link.SendDrive(new DriveCommand(l / max, r / max));

            if (kb.leftBracketKey.wasPressedThisFrame) link.PwmLimit = Mathf.Max(0.1f, link.PwmLimit - 0.1f);
            if (kb.rightBracketKey.wasPressedThisFrame) link.PwmLimit = Mathf.Min(1f, link.PwmLimit + 0.1f);
        }

        static float Axis(bool positive, bool negative) => (positive ? 1f : 0f) - (negative ? 1f : 0f);
    }
}
