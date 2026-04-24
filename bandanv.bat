@echo off
:: Destrabar los colores especiales (ANSI)
for /F %%a in ('echo prompt $E ^| cmd') do set "ESC=%%a"

:: Dejamos el fondo negro por defecto
color 07

:: Encabezado inicial
echo %ESC%[94m==================================================%ESC%[0m
echo %ESC%[97m              BandaNV v0.1 by Nacho%ESC%[0m
echo %ESC%[94m==================================================%ESC%[0m
echo.
echo %ESC%[94m      Ordenando archivos, espera un segundo...%ESC%[0m
echo.

:: Ejecucion del script de PowerShell
powershell.exe -ExecutionPolicy Bypass -File "C:\Users\Nacho\Scripts\BandaNV\bandanv.ps1"

:: El bucle del timer animado
for /L %%i in (5,-1,1) do (
    cls
    echo %ESC%[94m==================================================%ESC%[0m
    echo %ESC%[97m              BandaNV v0.1 by Nacho%ESC%[0m
    echo %ESC%[94m==================================================%ESC%[0m
    echo.
    echo %ESC%[94m==================================================%ESC%[0m
    echo %ESC%[97m       BANDA DE ARCHIVOS. Tarea finalizada.%ESC%[0m
    echo %ESC%[94m==================================================%ESC%[0m
    echo.
    echo %ESC%[91mEsta ventana se cerrara en %%i segundos...%ESC%[0m
    timeout /t 1 >nul
)

exit