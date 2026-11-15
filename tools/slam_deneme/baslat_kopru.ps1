# SLAM köprüsünü WSL içinde başlatır (sunucu kipi, port 9760). Önce kurulum.sh + kurulum_2.sh çalışmış olmalı (bkz. BENIOKU.md).
# Kullanım: powershell -File baslat_kopru.ps1 [-Port 9760] [-Mod harita|konum] [-Harita cikti/g1/harita.msg]
param([int]$Port = 9760, [string]$Mod = "harita", [string]$Harita = "")
$kok = (Resolve-Path "$PSScriptRoot\..\..").Path
$wslKok = "/mnt/c/" + $kok.Substring(3).Replace("\", "/")
$ayar = "$wslKok/tools/slam_deneme/ayar_ekdortgen.yaml"
$cmd = "cd /root/slam && ./kopru/build/kopru --vocab orb_vocab.fbow --config $ayar --mask veri/olcum_ham.maske.png --port $Port --mod $Mod --log warn"
if ($Harita -ne "") { $cmd += " --map-in $Harita" }
wsl -d Ubuntu-22.04 -u root -- bash -c $cmd
