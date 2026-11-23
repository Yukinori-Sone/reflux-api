@echo off
rem Build the distribution set into dist\: Reflux (the core, self-contained) with its tray tool RefluxAPIManager.exe, and a zip of it.
rem The core targets .NET Core 3.1, whose runtime is out of support, so it always includes the runtime.
rem dist\ is ignored by git.
rem Usage: build-dist.bat (in the repository root), or distribution\build-dist.cmd
setlocal
rem Keep this file ASCII only: cmd reads it in the console code page, which may not be cp932
cd /d "%~dp0.."
set DIST=%CD%\dist

for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "([xml](Get-Content 'Reflux\Reflux.csproj')).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1"`) do set CORE_VER=%%v
for /f "usebackq delims=" %%d in (`powershell -NoProfile -Command "Get-Date -Format yyyyMMdd"`) do set BUILD_DATE=%%d
set CORE_NAME=Reflux-%CORE_VER%+custom.%BUILD_DATE%
echo Core   %CORE_NAME%

if exist "%DIST%\Reflux" rmdir /s /q "%DIST%\Reflux"
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

echo.
echo === Tray tool (RefluxAPIManager, .NET Framework 4.8, nothing to install) ===
dotnet build RefluxAPIManager\RefluxAPIManager.csproj -c Release -nologo -v q -o "%DIST%\manager-build"
if errorlevel 1 goto :failed
copy /y "%DIST%\manager-build\RefluxAPIManager.exe" "%DIST%\Reflux\" >nul
copy /y "%DIST%\manager-build\RefluxAPIManager.exe.config" "%DIST%\Reflux\" >nul
rmdir /s /q "%DIST%\manager-build"

echo.
echo === Zip ===
del /q "%DIST%\Reflux-*.zip" 2>nul
powershell -NoProfile -Command "Compress-Archive -Path '%DIST%\Reflux' -DestinationPath '%DIST%\%CORE_NAME%.zip' -Force"
if errorlevel 1 goto :failed

echo.
echo Done: %DIST%
dir /b "%DIST%"
exit /b 0

:failed
echo.
echo Build failed.
exit /b 1
