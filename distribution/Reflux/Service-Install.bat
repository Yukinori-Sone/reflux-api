@echo off
rem Register Reflux.exe in this folder as the Windows service "Reflux API Server" (RefluxAPIServer) and start it.
rem The service starts with Windows (delayed start), runs without a window as LocalSystem and is restarted if it stops.
rem Run as administrator (right click, Run as administrator).
setlocal
set SVC=RefluxAPIServer
set EXE=%~dp0Reflux.exe

net session >nul 2>&1
if errorlevel 1 (
  echo Please run this file as administrator.
  pause
  exit /b 1
)
if not exist "%EXE%" (
  echo Reflux.exe not found next to this file.
  pause
  exit /b 1
)
sc query %SVC% >nul 2>&1
if not errorlevel 1 (
  echo The service %SVC% is already registered. Run Service-UnInstall.bat first to register it again.
  pause
  exit /b 1
)
powershell -NoProfile -Command "if (Get-Process Reflux -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $env:EXE }) { exit 1 }"
if errorlevel 1 (
  echo Reflux.exe in this folder is running. Close it first: only one Reflux can run from a folder.
  echo If it starts from a shortcut in the Startup folder, remove that shortcut too.
  pause
  exit /b 1
)

sc create %SVC% binPath= "\"%EXE%\" --service" start= delayed-auto DisplayName= "Reflux API Server"
if errorlevel 1 (
  echo Could not register the service.
  pause
  exit /b 1
)
sc description %SVC% "Reads beatmania IIDX INFINITAS play results and scores (read only) and serves them over HTTP. Reflux custom build." >nul
sc failure %SVC% reset= 86400 actions= restart/60000/restart/60000/restart/60000 >nul
rem Let users signed in at this PC start and stop the service (RefluxAPIServerMonitor.exe then works without a UAC prompt)
powershell -NoProfile -Command "$s = ((sc.exe sdshow %SVC%) | Where-Object { $_ -match 'D:' }).Trim(); $ace = '(A;;CCLCSWRPWPDTLOCRRC;;;IU)'; if (-not $s.Contains($ace)) { $s = $s -replace '\(A;;[A-Z]*;;;IU\)', $ace; if (-not $s.Contains($ace)) { $s = $s -replace '^D:', ('D:' + $ace) } }; sc.exe sdset %SVC% $s | Out-Null; exit $LASTEXITCODE"
if errorlevel 1 echo Could not allow users to start and stop the service: the monitor will ask for administrator rights instead.
sc start %SVC%
echo.
echo Registered and started. Check it with: sc query %SVC%
echo Remove it with Service-UnInstall.bat.
rem Start the tray monitor as the signed-in user (not elevated): it shows the state and settings and starts at logon from now on
if exist "%~dp0RefluxAPIServerMonitor.exe" (
  explorer.exe "%~dp0RefluxAPIServerMonitor.exe"
  echo The tray monitor RefluxAPIServerMonitor.exe was started.
)
pause
exit /b 0
