@echo off
rem Stop and remove the Windows service "Reflux API Server" (RefluxAPIServer). Files in this folder are left as they are.
rem Run as administrator (right click, Run as administrator).
setlocal
set SVC=RefluxAPIServer

net session >nul 2>&1
if errorlevel 1 (
  echo Please run this file as administrator.
  pause
  exit /b 1
)
sc query %SVC% >nul 2>&1
if errorlevel 1 (
  echo The service %SVC% is not registered.
  pause
  exit /b 0
)
sc stop %SVC% >nul 2>&1
for /l %%i in (1,1,15) do (
  sc query %SVC% | find "STOPPED" >nul && goto :stopped
  timeout /t 1 /nobreak >nul
)
:stopped
sc delete %SVC%
echo.
echo Removed. Start Reflux.exe directly (or from a Startup shortcut) from now on.
pause
exit /b 0
