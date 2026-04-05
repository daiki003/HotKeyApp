@echo off
setlocal
echo Closing HotKeyCommandApp...

:: Kill the process by name
taskkill /f /im HotKeyCommandApp.exe >nul 2>&1

if %ERRORLEVEL% EQU 0 (
    echo HotKeyCommandApp has been closed.
) else (
    echo HotKeyCommandApp was not running or could not be closed.
)

:: Wait for a moment so the user can see the message
timeout /t 2 >nul
exit
