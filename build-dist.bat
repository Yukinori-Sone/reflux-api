@echo off
rem Build the distribution set: dist\Reflux (core) and dist\RefluxScoreManager (client), and a zip of each.
rem Both are self-contained (no .NET install needed). dist\ is ignored by git.
rem Usage: build-dist.bat
setlocal
rem Keep this file ASCII only: cmd reads it in the console code page, which may not be cp932
cd /d "%~dp0"
set DIST=%~dp0dist

for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "([xml](Get-Content 'Reflux\Reflux.csproj')).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1"`) do set CORE_VER=%%v
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "([xml](Get-Content 'RefluxScoreManager\RefluxScoreManager.csproj')).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1"`) do set CLIENT_VER=%%v
for /f "usebackq delims=" %%d in (`powershell -NoProfile -Command "Get-Date -Format yyyyMMdd"`) do set BUILD_DATE=%%d
set CORE_NAME=Reflux-%CORE_VER%+custom.%BUILD_DATE%
set CLIENT_NAME=RefluxScoreManager-%CLIENT_VER%+%BUILD_DATE%
echo Core   %CORE_NAME%
echo Client %CLIENT_NAME%

if exist "%DIST%\Reflux" rmdir /s /q "%DIST%\Reflux"
if exist "%DIST%\RefluxScoreManager" rmdir /s /q "%DIST%\RefluxScoreManager"
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

echo.
echo === Client (RefluxScoreManager) ===
dotnet publish RefluxScoreManager\RefluxScoreManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%DIST%\RefluxScoreManager" -nologo -v q
if errorlevel 1 goto :failed
del /q "%DIST%\RefluxScoreManager\*.pdb" 2>nul
copy /y "LICENSE" "%DIST%\RefluxScoreManager\" >nul
copy /y "distribution\RefluxScoreManager\*.txt" "%DIST%\RefluxScoreManager\" >nul

echo.
echo === Zip ===
del /q "%DIST%\*.zip" 2>nul
powershell -NoProfile -Command "Compress-Archive -Path '%DIST%\Reflux' -DestinationPath '%DIST%\%CORE_NAME%.zip' -Force; Compress-Archive -Path '%DIST%\RefluxScoreManager' -DestinationPath '%DIST%\%CLIENT_NAME%.zip' -Force"
if errorlevel 1 goto :failed

echo.
echo Done: %DIST%
dir /b "%DIST%"
exit /b 0

:failed
echo.
echo Build failed.
exit /b 1
