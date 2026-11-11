@echo off
REM Opcion B (sin internet): copia aqui los archivos sface.parte1 y sface.parte2 y ejecuta este archivo.
cd /d "%~dp0"
if not exist sface.parte1 (echo  [X] Falta sface.parte1 en esta carpeta & pause & exit /b 1)
if not exist sface.parte2 (echo  [X] Falta sface.parte2 en esta carpeta & pause & exit /b 1)
copy /b sface.parte1 + sface.parte2 "AsistenciaFacial\modelos\face_recognition_sface_2021dec.onnx" >nul
echo  Listo: modelo unido en AsistenciaFacial\modelos\
pause
