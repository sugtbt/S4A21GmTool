@echo off
setlocal

cd /d "%~dp0"

set "APP=bin\Debug\net10.0\DfoGmTool.exe"
set "PORT=5051"

if exist "config.ini" (
    for /f "usebackq tokens=1,* delims==" %%A in ("config.ini") do (
        if /i "%%~A"=="listen_port" set "PORT=%%~B"
    )
)

set "PORT=%PORT: =%"
if "%PORT%"=="" set "PORT=5051"
set "URL=http://localhost:%PORT%"

if not exist "%APP%" (
    echo DfoGmTool.exe was not found:
    echo   %CD%\%APP%
    echo.
    echo Build it first:
    echo   dotnet build DfoGmToolA21.sln -c Debug
    echo.
    pause
    exit /b 1
)

echo Starting DfoGmToolA21...
echo Working directory: %CD%
echo URL: %URL%
echo.
echo Keep this window open while using the GM tool.
echo Press Ctrl+C in this window to stop it.
echo.

if /i not "%~1"=="--no-browser" (
    start "Open GM Tool" /min cmd /c "timeout /t 2 /nobreak >nul && rundll32 url.dll,FileProtocolHandler %URL%"
)

"%APP%"
set "EXITCODE=%ERRORLEVEL%"

echo.
echo DfoGmTool exited with code %EXITCODE%.
pause
exit /b %EXITCODE%
