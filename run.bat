@echo off
chcp 65001 >nul
setlocal

rem =============================================================
rem  اجرای سایت UP2AI روی همین کامپیوتر.
rem
rem  روی این فایل دوبار کلیک کن. اگر رمز پنل ساخته نشده باشد، اول همان را
rem  می‌سازد و بعد سایت را بالا می‌آورد و مرورگر را باز می‌کند.
rem =============================================================

cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 goto NoDotnet

if exist ".env" goto RunApp

echo.
echo -- هنوز رمز پنل مدیریت ساخته نشده --------------------------------
echo   الان رمزت را می‌پرسد. هرچه بزنی فقط روی همین سیستم می‌ماند.
echo   بعدش دو خطی که چاپ می‌کند را در فایلی به نام .env همین‌جا بگذار.
echo.
dotnet run --no-launch-profile -- hash-password
echo.
echo   وقتی .env را ساختی، دوباره روی run.bat کلیک کن.
echo.
pause
exit /b 0

:RunApp
echo.
echo -- در حال بالا آوردن سایت -----------------------------------------
echo   آدرس سایت:  http://localhost:5199
echo   پنل مدیریت: http://localhost:5199/admin
echo.
echo   برای خاموش کردن، این پنجره را ببند یا Ctrl+C بزن.
echo.

rem محیط Development تا کوکی نشست روی http ساده هم کار کند (روی هاست
rem واقعی که HTTPS دارد این لازم نیست).
set ASPNETCORE_ENVIRONMENT=Development
set ASPNETCORE_URLS=http://localhost:5199

start "" http://localhost:5199
dotnet run --no-launch-profile

pause
exit /b 0

:NoDotnet
echo.
echo [!] دات‌نت روی این سیستم پیدا نشد.
echo     از اینجا نسخه‌ی .NET 8 SDK را نصب کن و دوباره امتحان کن:
echo     https://dotnet.microsoft.com/download/dotnet/8.0
echo.
pause
exit /b 1
