# Robotu (Jetson) bulunduğu ağda bulur.
# Önce jetson.local adını dener; çalışmazsa (ör. okul ağı) bilgisayarın alt ağını tarar ve
# robot panelinin /api/durum cevabından robotu tanır. Bulduğu adresi ~/.ssh/config içindeki
# "Host jetson" kaydına yazar; böylece "ssh jetson" ve Jetson Ekranı her ağda çalışır.
#
#   robotu_bul.ps1          adresi bulur, SSH ayarını günceller, adresi yazar
#   robotu_bul.ps1 -Panel   ayrıca paneli tarayıcıda açar
param([switch]$Panel)

$ErrorActionPreference = "Stop"
$sshConfig = Join-Path $env:USERPROFILE ".ssh\config"

function Robot-mu($ip) {
    try {
        $d = Invoke-RestMethod "http://$ip/api/durum" -TimeoutSec 2
        return [bool]$d.kurulum_agi
    } catch { return $false }
}

function Ada-Gore-Bul {
    try {
        $ip = [System.Net.Dns]::GetHostAddresses("jetson.local") |
            Where-Object AddressFamily -eq "InterNetwork" | Select-Object -First 1
        if ($ip -and (Robot-mu $ip.IPAddressToString)) { return $ip.IPAddressToString }
    } catch {}
    return $null
}

function Agi-Tara {
    $yerel = Get-NetIPAddress -AddressFamily IPv4 |
        Where-Object { $_.PrefixOrigin -in "Dhcp", "Manual" -and $_.IPAddress -notlike "169.254.*" -and $_.InterfaceAlias -notlike "*vEthernet*" }
    foreach ($a in $yerel) {
        $onek = ($a.IPAddress -split "\.")[0..2] -join "."
        # 80. portu açık olanları aynı anda dene (yarım saniye bekleme)
        $denemeler = 1..254 | Where-Object { "$onek.$_" -ne $a.IPAddress } | ForEach-Object {
            $c = New-Object System.Net.Sockets.TcpClient
            [pscustomobject]@{ Ip = "$onek.$_"; Istemci = $c; Is = $c.ConnectAsync("$onek.$_", 80) }
        }
        Start-Sleep -Milliseconds 700
        $acik = $denemeler | Where-Object { $_.Is.Status -eq "RanToCompletion" -and $_.Istemci.Connected } | ForEach-Object Ip
        $denemeler | ForEach-Object { $_.Istemci.Dispose() }
        foreach ($ip in $acik) { if (Robot-mu $ip) { return $ip } }
    }
    return $null
}

function Ssh-Ayarini-Guncelle($ip) {
    if (-not (Test-Path $sshConfig)) { return }
    $satirlar = Get-Content $sshConfig
    $icinde = $false
    $yeni = foreach ($s in $satirlar) {
        if ($s -match "^\s*Host\s+") { $icinde = ($s -match "^\s*Host\s+jetson\s*$") }
        if ($icinde -and $s -match "^\s*HostName\s+") { "    HostName $ip" } else { $s }
    }
    Set-Content -Path $sshConfig -Value $yeni -Encoding ASCII
}

$ip = Ada-Gore-Bul
if (-not $ip) { $ip = Agi-Tara }
if (-not $ip) {
    Write-Output "Robot bulunamadi."
    if ($Panel) {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show("Robot bu ağda bulunamadı.`n`nBilgisayar ile robot aynı Wi-Fi ağında mı? Robot yeni açıldıysa 1-2 dakika bekleyip tekrar dene.", "Robot Paneli", "OK", "Warning") | Out-Null
    }
    exit 1
}
Ssh-Ayarini-Guncelle $ip
Write-Output $ip
if ($Panel) { Start-Process "http://$ip/" }
