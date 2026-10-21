@echo off
rem Double-click to deploy. Asks for Administrator rights, pulls the latest code, then runs deploy.ps1.
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

rem Pull first, so the newest version of deploy.ps1 itself is what runs.
echo ==^> Pulling latest code
rem safe.directory: running as Administrator on a clone owned by another user otherwise fails as "dubious ownership".
git -c safe.directory=* -C "%~dp0." pull --ff-only
if %errorlevel% neq 0 (
    echo.
    echo DEPLOY FAILED: git pull failed ^(is git installed? local changes or diverged branch?^)
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy.ps1"
echo.
pause
