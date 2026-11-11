@echo off
REM Crea la carpeta "publicado" con TODO incluido (no hace falta instalar .NET en la otra laptop).
REM Copia esa carpeta a cualquier laptop con Windows 10/11 de 64 bits y abre AsistenciaFacial.exe
cd /d "%~dp0"
dotnet publish AsistenciaFacial -c Release -r win-x64 --self-contained true -o publicado
if errorlevel 1 (pause & exit /b 1)
echo.
echo  Listo: carpeta "publicado\AsistenciaFacial.exe"
pause
