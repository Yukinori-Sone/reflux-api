@echo off
rem Distribution set whose client includes the .NET runtime (about 50 MB, runs without installing anything).
rem Output: dist\self-contained
call "%~dp0distribution\build-dist.cmd" self-contained
