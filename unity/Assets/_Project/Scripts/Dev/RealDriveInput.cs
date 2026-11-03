using ARaki.Robot;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ARaki.Dev
{
    /// <summary>
    /// Gerçek robotu PC'den ya da oyun kolundan sürmek için: W/S ileri-geri, A/D dönüş (ok tuşları da),
    /// sol çubuk, R/F güç sınırı, L ışık, H korna, X dörtlüler.
    /// Dönüş karışımı sürüş sayfasındakiyle aynı: kızaklı şasi ancak tekerler arasında büyük fark olunca
    /// döner, bu yüzden hareket halindeyken dış teker en az %60, iç teker ters yönde dışın çeyreği kadar döner.
    /// Geri giderken de aynı kural, yani araba gibi döner.
    /// </summary>
    [RequireComponent(typeof(RealRobotLink))]
    public class RealDriveInput : MonoBehaviour
    {
        const float TurnMinimum = 0.6f, InnerWheel = -0.25f, InPlace = 0.8f;

        RealRobotLink link;
        bool hazard;

        void Awake() => link = GetComponent<RealRobotLink>();

        void Update()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            float forward = 0f, turn = 0f;
            if (kb != null)
            {
                forward = Axis(kb.wKey.isPressed || kb.upArrowKey.isPressed, kb.sKey.isPressed || kb.downArrowKey.isPressed);
                turn = Axis(kb.dKey.isPressed || kb.rightArrowKey.isPressed, kb.aKey.isPressed || kb.leftArrowKey.isPressed);
            }
            if (pad != null && pad.leftStick.ReadValue().sqrMagnitude > 0.04f)
            {
                Vector2 s = pad.leftStick.ReadValue();
                forward = Mathf.Abs(s.y) > 0.2f ? s.y : 0f;
                turn = Mathf.Abs(s.x) > 0.3f ? Mathf.Sign(s.x) : 0f;
            }
            link.SendDrive(Mix(forward, turn));

            if (kb == null) return;
            if (kb.lKey.wasPressedThisFrame) link.SetLed(!link.LedOn);
            if (kb.hKey.wasPressedThisFrame) link.Horn(400);
            if (kb.xKey.wasPressedThisFrame) link.Hazard(hazard = !hazard);
            if (kb.fKey.wasPressedThisFrame) link.PwmLimit -= 0.1f;
            if (kb.rKey.wasPressedThisFrame) link.PwmLimit += 0.1f;
        }

        /// <summary>İleri (-1..1) ve dönüş (-1, 0, 1) → sol/sağ teker.</summary>
        public static DriveCommand Mix(float forward, float turn)
        {
            if (forward != 0f && turn != 0f)
            {
                float outer = Mathf.Sign(forward) * Mathf.Max(Mathf.Abs(forward), TurnMinimum);
                float inner = InnerWheel * outer;
                return turn > 0f ? new DriveCommand(outer, inner) : new DriveCommand(inner, outer);
            }
            if (turn != 0f) return new DriveCommand(turn * InPlace, -turn * InPlace);
            return new DriveCommand(forward, forward);
        }

        static float Axis(bool positive, bool negative) => (positive ? 1f : 0f) - (negative ? 1f : 0f);
    }
}
