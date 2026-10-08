namespace ARaki.Robot
{
    /// <summary>Sürüş komutu. Değerler -1..1 (tam geri .. tam ileri), PWM yüzdesine karşılık gelir.</summary>
    public struct DriveCommand
    {
        public float Left;
        public float Right;

        public DriveCommand(float left, float right)
        {
            Left = left;
            Right = right;
        }

        public static DriveCommand Stop => default;
    }

    /// <summary>Robottan gelen anlık durum. Sahte ve gerçek robot aynı yapıyı doldurur.</summary>
    public struct RobotTelemetry
    {
        public float Battery01;      // 0..1
        public float YawDegrees;     // pusula yönü, 0..360
        public float LeftSpeed;      // m/s, tekerlek yüzeyinde
        public float RightSpeed;
        public float ForwardSpeed;   // m/s
    }

    /// <summary>
    /// Oyunun robotla konuştuğu tek kapı. Oyun kodu yalnızca bunu bilir;
    /// arkasında MockRobotLink (simülasyon) ya da gerçek Wi-Fi bağlantısı durabilir.
    /// </summary>
    public interface IRobotLink
    {
        bool IsConnected { get; }
        RobotTelemetry Telemetry { get; }

        /// <summary>Yükseltmelerle değişen güç sınırı, 0..1. Motorlara giden en yüksek PWM oranı.</summary>
        float PwmLimit { get; set; }

        void SendDrive(DriveCommand command);
    }
}
