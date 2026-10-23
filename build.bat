@echo off
rem Builds danboo.exe, a self-contained program that runs without .NET installed, into
rem DanbooruDownloader.Cli\publish. Keep DanbooruProviderList.xml next to danboo.exe.
rem   build.bat            for this PC (x64 or ARM64)
rem   build.bat win-x86    for another .NET runtime identifier
setlocal
cd /d "%~dp0DanbooruDownloader.Cli"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo The .NET SDK was not found. Install .NET 10: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

set "RID=%~1"
if not defined RID (
    if /i "%PROCESSOR_ARCHITECTURE%"=="ARM64" (set "RID=win-arm64") else (set "RID=win-x64")
)

rem Native SQLite goes inside the exe too, so the folder holds only danboo.exe and the provider list.
dotnet publish -c Release -r %RID% --self-contained true --verbosity quiet -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:AssemblyName=danboo -p:DebugType=none -o publish
if errorlevel 1 (
    echo Build failed.
    if "%~1"=="" pause
    exit /b 1
)

echo.
echo Built: %CD%\publish\danboo.exe
echo Try it: publish\danboo.exe --help
