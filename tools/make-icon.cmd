@echo off
chcp 65001 >nul
rem Перерисовать app.ico после изменений в src\Logo.cs
cd /d "%~dp0.."
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist tools\bin mkdir tools\bin
"%CSC%" /nologo /codepage:65001 /out:tools\bin\MakeIcon.exe /r:System.Drawing.dll tools\MakeIcon.cs src\Logo.cs || exit /b 1
tools\bin\MakeIcon.exe app.ico icon-preview.png && echo Готово: app.ico, icon-preview.png