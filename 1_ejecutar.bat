@echo off
title Asistencia IAR-600 (C#)
cd /d "%~dp0"
where dotnet >nul 2>nul || (
  echo  [X] No se encontro .NET. Instala el ".NET 8 SDK" (o Visual Studio con "Desarrollo de escritorio de .NET").
  echo      https://dotnet.microsoft.com/download/dotnet/8.0
  pause & exit /b 1
)
echo  Compilando y abriendo el programa (la primera vez descarga las librerias, necesita internet)...
dotnet run --project AsistenciaFacial -c Release
if errorlevel 1 pause
