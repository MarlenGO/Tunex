@echo off
chcp 65001 >nul
rem Вернуть прежний дизайн (бирюзовый, v1): копирует сохранённые исходники и иконку, пересобирает Tunex.exe
rem Внимание: правки функций, сделанные после смены дизайна, тоже откатятся. Текущие файлы сохраняются в design-backup\before-restore
cd /d "%~dp0"
echo Вернуть старый дизайн? Текущие файлы будут сохранены в design-backup\before-restore
pause
if not exist design-backup\before-restore mkdir design-backup\before-restore
copy /y src\*.cs design-backup\before-restore\ >nul
copy /y app.ico design-backup\before-restore\ >nul
copy /y icon-preview.png design-backup\before-restore\ >nul
copy /y design-backup\v1-teal\src\*.cs src\ >nul
copy /y design-backup\v1-teal\app.ico . >nul
copy /y design-backup\v1-teal\icon-preview.png . >nul
call build.cmd