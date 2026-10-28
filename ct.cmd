@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\ct.ps1" %*
exit /b %ERRORLEVEL%
