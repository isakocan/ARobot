# Jetson'ın masaüstünü bu bilgisayarda açar.
# VNC doğrudan ağa açık değil; SSH tüneli (ssh jetson) üzerinden geçer.
$ErrorActionPreference = "Stop"
$yerelPort = 5909
$viewer = "C:\Program Files\TigerVNC\vncviewer.exe"

Add-Type -AssemblyName System.Windows.Forms
function Uyari($mesaj) {
    [System.Windows.Forms.MessageBox]::Show($mesaj, "Jetson Ekranı", "OK", "Warning") | Out-Null
}

if (-not (Test-Path $viewer)) { Uyari "TigerVNC bulunamadı: $viewer"; exit 1 }

# Robotun bu ağdaki adresini bul ve SSH ayarına yaz
& (Join-Path $PSScriptRoot "robotu_bul.ps1") | Out-Null
if ($LASTEXITCODE -ne 0) {
    Uyari "Robot bu ağda bulunamadı.`n`nBilgisayar ile Jetson aynı Wi-Fi ağında mı? Jetson yeni açıldıysa 1-2 dakika bekleyip tekrar dene."
    exit 1
}

$tunel = Start-Process ssh -ArgumentList "-N", "-o", "ExitOnForwardFailure=yes", "-o", "ConnectTimeout=8",
    "-L", "${yerelPort}:localhost:5900", "jetson" -WindowStyle Hidden -PassThru

# Tünel açılana kadar bekle (en çok ~10 sn)
$hazir = $false
for ($i = 0; $i -lt 20 -and -not $tunel.HasExited; $i++) {
    Start-Sleep -Milliseconds 500
    if (Test-NetConnection localhost -Port $yerelPort -InformationLevel Quiet -WarningAction SilentlyContinue) { $hazir = $true; break }
}
if (-not $hazir) {
    if (-not $tunel.HasExited) { Stop-Process $tunel -Force }
    Uyari "Jetson'a bağlanılamadı.`n`nBilgisayar ile Jetson aynı Wi-Fi ağında mı? Jetson yeni açıldıysa 1-2 dakika bekleyip tekrar dene."
    exit 1
}

try {
    Start-Process $viewer -ArgumentList "-RemoteResize=0", "localhost::$yerelPort" -Wait
} finally {
    if (-not $tunel.HasExited) { Stop-Process $tunel -Force }
}
