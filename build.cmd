@echo off
chcp 65001 >nul
rem Сборка без Visual Studio и NuGet: встроенный в Windows компилятор C# (.NET Framework 4.x)
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist bin mkdir bin
rem app.ico генерируется из src\Logo.cs: tools\make-icon.cmd
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:bin\Tunex.exe /win32manifest:app.manifest /win32icon:app.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll ^
  src\*.cs
if errorlevel 1 (echo. & echo Сборка не удалась & exit /b 1)
echo Готово: bin\Tunex.exe
