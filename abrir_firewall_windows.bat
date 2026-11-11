@echo off
REM Clic derecho -> "Ejecutar como administrador". Permite que los celulares entren por el puerto 5000.
netsh advfirewall firewall add rule name="Asistencia IAR-600 (puerto 5000)" dir=in action=allow protocol=TCP localport=5000 profile=any
echo.
echo  Si dice "Correcto", los celulares ya pueden entrar por el puerto 5000.
pause
