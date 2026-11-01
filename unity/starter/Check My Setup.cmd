@echo off
rem Looks at this PC and says what's still needed for making regions. It changes nothing.
rem Double-click it from File Explorer: other apps from the Store can have private folders of their own.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Check My Setup.ps1"
pause
