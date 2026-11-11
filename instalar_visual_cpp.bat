@echo off
REM OpenCV necesita el "Microsoft Visual C++ Redistributable 2015-2022 (x64)".
REM Sin el, NO funciona ninguna fuente de imagen (ni camara, ni fotos).
cd /d "%~dp0"
echo  Descargando Visual C++ Redistributable x64 (necesita internet una vez)...
powershell -NoProfile -Command "Invoke-WebRequest -Uri 'https://aka.ms/vs/17/release/vc_redist.x64.exe' -OutFile '%TEMP%\vc_redist.x64.exe'"
if not exist "%TEMP%\vc_redist.x64.exe" (echo  [X] No se pudo descargar. Abre en el navegador: https://aka.ms/vs/17/release/vc_redist.x64.exe & pause & exit /b 1)
"%TEMP%\vc_redist.x64.exe" /install /passive /norestart
echo  Listo. Reinicia la laptop y vuelve a abrir el programa.
pause
