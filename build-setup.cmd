@echo off
rem Installer for friends: dist\Tunex-Setup.exe (contains a fresh bin\Tunex.exe)
setlocal
cd /d "%~dp0"
call "%~dp0build.cmd" || exit /b 1
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%~dp0dist" mkdir "%~dp0dist"
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:"%~dp0dist\Tunex-Setup.exe" /win32manifest:"%~dp0app.manifest" /win32icon:"%~dp0app.ico" /resource:"%~dp0bin\Tunex.exe",Tunex.exe /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "%~dp0tools\Setup.cs"
if errorlevel 1 (echo. & echo Setup build failed & exit /b 1)
echo Done: dist\Tunex-Setup.exe