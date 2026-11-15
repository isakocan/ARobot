@echo off
rem Gelistirici ekranini acar: sunucu calismiyorsa baslatir, hazir olunca tarayicida sayfayi acar.
rem Kullanim: ac.cmd [robot-adresi]   (verilmezse asagidaki varsayilan)
rem Bu pencereyi kapatmak sunucuyu durdurur.
setlocal
set "ROBOT=%~1"
if "%ROBOT%"=="" set "ROBOT=172.30.45.67"
set "KAYIT=C:\Users\PC\Desktop\ARobot\kayitlar\2026-11-14_20-12-25_olcum_ham"
set "PORT=8765"
cd /d "%~dp0..\.."

powershell -NoProfile -Command "try { Invoke-WebRequest -UseBasicParsing http://127.0.0.1:%PORT%/api/durum -TimeoutSec 2 | Out-Null; exit 0 } catch { exit 1 }"
if %errorlevel%==0 (
  echo Sunucu zaten calisiyor, sayfa aciliyor.
  start "" "http://127.0.0.1:%PORT%/"
  exit /b 0
)

start "" /min powershell -NoProfile -WindowStyle Hidden -Command "for ($i = 0; $i -lt 120; $i++) { try { Invoke-WebRequest -UseBasicParsing http://127.0.0.1:%PORT%/api/durum -TimeoutSec 1 | Out-Null; Start-Process http://127.0.0.1:%PORT%/; break } catch { Start-Sleep -Seconds 1 } }"

set "SECENEK=--robot %ROBOT%"
if exist "%KAYIT%\bilgi.json" set "SECENEK=--kayit "%KAYIT%" --robot %ROBOT%"
echo Gelistirici ekrani baslatiliyor (yaklasik 20 sn). Robot: %ROBOT%
python tools\gelistirici_ekrani\calistir.py %SECENEK% --port %PORT%
echo.
echo Sunucu durdu.
pause
