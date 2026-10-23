@echo off
rem Runs the danboo CLI. No arguments (or a double-click): the interactive menu.
rem With arguments, they go to danboo, e.g.:
rem   run.bat search --provider danbooru --tags touhou
rem   run.bat --help
rem Runs inside DanbooruDownloader.Cli: a .env, downloaded.db or downloads\ already in that
rem folder is used; otherwise data goes to %LOCALAPPDATA%\DanbooruDownloader and images to
rem Pictures\DanbooruDownloader ("danboo --help" shows both).
setlocal
cd /d "%~dp0DanbooruDownloader.Cli"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo The .NET SDK was not found. Install .NET 10: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

rem UTF-8 console, so Japanese tag names print correctly.
chcp 65001 >nul

dotnet run -c Release --verbosity quiet -- %*
set EXITCODE=%ERRORLEVEL%

rem Keep a double-clicked window open if something failed.
if "%~1"=="" if not "%EXITCODE%"=="0" pause
exit /b %EXITCODE%
