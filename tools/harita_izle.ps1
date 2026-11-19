# Bir robot kaydından stella_vslam_dense ile haritayı canlı izletir (tarayıcıda 3B görüntüleyici).
# Kayıt seçilir, 360 video yoksa hazırlanır (tools/haritalama/esdikdortgen.py), WSL'de haritalama başlar,
# sayfa hazır olunca tarayıcı açılır. Video bitince harita ekranda kalır; sayfadaki "Terminate SLAM"
# haritayı kaydeder (kayıt\harita\yogun.ply) ve kapatır.
$ErrorActionPreference = "Stop"
$Host.UI.RawUI.WindowTitle = "Harita İzle"
$port = 8080

# kayitlar klasörü: bu betiğin üst klasörlerinden birinde (ana çalışma kopyası)
$kok = $PSScriptRoot
while ($kok -and -not (Test-Path (Join-Path $kok "kayitlar"))) { $kok = Split-Path $kok -Parent }
if (-not $kok) { Write-Host "kayitlar klasörü bulunamadı." -ForegroundColor Red; Read-Host "Kapatmak için Enter"; exit 1 }
$kayitlar = @(Get-ChildItem (Join-Path $kok "kayitlar") -Directory | Where-Object { Test-Path (Join-Path $_.FullName "goruntu.h264") } | Sort-Object Name)
if ($kayitlar.Count -eq 0) { Write-Host "Kayıt yok." -ForegroundColor Red; Read-Host "Kapatmak için Enter"; exit 1 }

Write-Host ""
Write-Host "  Hangi kayıt haritalansın?" -ForegroundColor Cyan
for ($i = 0; $i -lt $kayitlar.Count; $i++) {
    $hazir = if (Test-Path (Join-Path $kayitlar[$i].FullName "harita\esdikdortgen.mp4")) { "" } else { "  (ilk sefer: video hazırlanacak)" }
    Write-Host ("   {0,2}) {1}{2}" -f ($i + 1), $kayitlar[$i].Name, $hazir)
}
$secim = Read-Host "`n  Numara"
$n = 0
if (-not [int]::TryParse($secim, [ref]$n) -or $n -lt 1 -or $n -gt $kayitlar.Count) { Write-Host "Geçersiz seçim." -ForegroundColor Red; Start-Sleep 2; exit 1 }
$kayit = $kayitlar[$n - 1].FullName

if (Test-NetConnection localhost -Port $port -InformationLevel Quiet -WarningAction SilentlyContinue) {
    Write-Host "`n  $port portu dolu: başka bir harita penceresi açık olabilir. Önce onu kapat." -ForegroundColor Yellow
    Read-Host "  Kapatmak için Enter"; exit 1
}

$araclar = Join-Path $PSScriptRoot "haritalama"
if (-not (Test-Path (Join-Path $kayit "harita\esdikdortgen.mp4"))) {
    Write-Host "`n  360 video hazırlanıyor (1-2 dakika)..." -ForegroundColor Cyan
    python (Join-Path $araclar "esdikdortgen.py") $kayit
    if ($LASTEXITCODE -ne 0) { Write-Host "Video hazırlanamadı." -ForegroundColor Red; Read-Host "Kapatmak için Enter"; exit 1 }
}

# sayfa açılınca tarayıcıyı aç (arka planda bekler)
Start-Job -ArgumentList $port {
    param($p)
    for ($i = 0; $i -lt 240; $i++) {
        Start-Sleep -Milliseconds 500
        if (Test-NetConnection localhost -Port $p -InformationLevel Quiet -WarningAction SilentlyContinue) { Start-Process "http://localhost:$p"; break }
    }
} | Out-Null

$wslKayit = (wsl -d Ubuntu-22.04 -- wslpath -a ($kayit -replace '\\', '/')).Trim()
$wslBetik = (wsl -d Ubuntu-22.04 -- wslpath -a ((Join-Path $araclar "calistir.sh") -replace '\\', '/')).Trim()
Write-Host "`n  Haritalama başlıyor, tarayıcı birazdan açılacak (http://localhost:$port)." -ForegroundColor Cyan
Write-Host "  Bitirince sayfadaki 'Terminate SLAM' düğmesine bas: harita kaydedilir ve bu pencere kapanır.`n"
wsl -d Ubuntu-22.04 -- bash $wslBetik $wslKayit --izle
Get-Job | Remove-Job -Force
Write-Host "`n  Bitti. Çıktılar: $kayit\harita" -ForegroundColor Green
Start-Sleep 3
