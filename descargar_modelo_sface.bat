@echo off
REM Opcion A (con internet): descarga el modelo SFace (37 MB) del OpenCV Model Zoo.
cd /d "%~dp0"
powershell -NoProfile -Command "Invoke-WebRequest -Uri 'https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/face_recognition_sface/face_recognition_sface_2021dec.onnx' -OutFile 'AsistenciaFacial\modelos\face_recognition_sface_2021dec.onnx'"
if exist "AsistenciaFacial\modelos\face_recognition_sface_2021dec.onnx" (echo  Listo: modelo descargado.) else (echo  [X] No se pudo descargar. Usa la opcion B: unir_modelo_sface.bat)
pause
