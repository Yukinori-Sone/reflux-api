@echo off
rem Distribution set whose client needs the .NET 9 Desktop Runtime (about 2.5 MB).
rem The core is self-contained in both sets. Output: dist\framework-dependent
call "%~dp0distribution\build-dist.cmd" framework-dependent
