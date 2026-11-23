@echo off
rem Build the distribution set into dist\<mode>: Reflux (core with its tray tool RefluxAPIManager.exe), BeatLedger (client) and a zip of each.
rem Called by build-dist-self-contained.bat / build-dist-framework-dependent.bat in the repository root.
rem   self-contained       : the client includes the .NET runtime (about 50 MB, nothing to install)
rem   framework-dependent  : the client needs the .NET 9 Desktop Runtime (about 2.5 MB)
rem The core is always self-contained: it targets .NET Core 3.1, whose runtime is out of support.
rem dist\ is ignored by git.
rem Usage: distribution\build-dist.cmd self-contained|framework-dependent
setlocal
rem Keep this file ASCII only: cmd reads it in the console code page, which may not be cp932
set MODE=%~1
if /i not "%MODE%"=="self-contained" if /i not "%MODE%"=="framework-dependent" (
  echo Usage: %~nx0 self-contained^|framework-dependent
  exit /b 1
)
cd /d "%~dp0.."
set DIST=%CD%\dist\%MODE%

for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "([xml](Get-Content 'Reflux\Reflux.csproj')).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1"`) do set CORE_VER=%%v
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "([xml](Get-Content 'BeatLedger\BeatLedger.csproj')).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1"`) do set CLIENT_VER=%%v
for /f "usebackq delims=" %%d in (`powershell -NoProfile -Command "Get-Date -Format yyyyMMdd"`) do set BUILD_DATE=%%d
set CORE_NAME=Reflux-%CORE_VER%+custom.%BUILD_DATE%
set CLIENT_NAME=BeatLedger-%CLIENT_VER%+%BUILD_DATE%
if /i "%MODE%"=="framework-dependent" set CLIENT_NAME=%CLIENT_NAME%-needs-dotnet9
echo Core   %CORE_NAME%
echo Client %CLIENT_NAME%

if exist "%DIST%\Reflux" rmdir /s /q "%DIST%\Reflux"
if exist "%DIST%\BeatLedger" rmdir /s /q "%DIST%\BeatLedger"
if not exist "%DIST%" mkdir "%DIST%"

echo.
echo === Core (Reflux) ===
dotnet publish Reflux\Reflux.csproj -c Release -r win-x64 --self-contained true -o "%DIST%\Reflux" -nologo -v q
if errorlevel 1 goto :failed
del /q "%DIST%\Reflux\*.pdb" 2>nul
mkdir "%DIST%\Reflux\docs"
copy /y "docs\*_Original.md" "%DIST%\Reflux\docs\" >nul
copy /y "docs\*_Custom.md" "%DIST%\Reflux\docs\" >nul
copy /y "docs\API*.md" "%DIST%\Reflux\docs\" >nul
copy /y "Reflux\*.html" "%DIST%\Reflux\" >nul
copy /y "Reflux\segment-display.js" "%DIST%\Reflux\" >nul
copy /y "LICENSE" "%DIST%\Reflux\" >nul
copy /y "distribution\Reflux\*.txt" "%DIST%\Reflux\" >nul
copy /y "distribution\Reflux\*.bat" "%DIST%\Reflux\" >nul
rem Tray tool next to Reflux.exe (.NET Framework 4.8, nothing to install)
dotnet build RefluxAPIManager\RefluxAPIManager.csproj -c Release -nologo -v q -o "%DIST%\manager-build"
if errorlevel 1 goto :failed
copy /y "%DIST%\manager-build\RefluxAPIManager.exe" "%DIST%\Reflux\" >nul
copy /y "%DIST%\manager-build\RefluxAPIManager.exe.config" "%DIST%\Reflux\" >nul
rmdir /s /q "%DIST%\manager-build"

echo.
echo === Client (BeatLedger) ===
if /i "%MODE%"=="self-contained" (
  dotnet publish BeatLedger\BeatLedger.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%DIST%\BeatLedger" -nologo -v q
) else (
  dotnet publish BeatLedger\BeatLedger.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%DIST%\BeatLedger" -nologo -v q
)
if errorlevel 1 goto :failed
del /q "%DIST%\BeatLedger\*.pdb" 2>nul
copy /y "LICENSE" "%DIST%\BeatLedger\" >nul
copy /y "distribution\BeatLedger\*.txt" "%DIST%\BeatLedger\" >nul
if /i "%MODE%"=="framework-dependent" copy /y "distribution\framework-dependent\*.txt" "%DIST%\BeatLedger\" >nul

echo.
echo === Zip ===
del /q "%DIST%\*.zip" 2>nul
powershell -NoProfile -Command "Compress-Archive -Path '%DIST%\Reflux' -DestinationPath '%DIST%\%CORE_NAME%.zip' -Force; Compress-Archive -Path '%DIST%\BeatLedger' -DestinationPath '%DIST%\%CLIENT_NAME%.zip' -Force"
if errorlevel 1 goto :failed

echo.
echo Done: %DIST%
dir /b "%DIST%"
exit /b 0

:failed
echo.
echo Build failed.
exit /b 1
