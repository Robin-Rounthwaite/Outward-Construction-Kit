@echo off
rem For when Unity Hub opens a project and Unity closes again with "No valid Unity Editor license found".
rem The Microsoft Store version of the Hub keeps your licence in a private folder. Unity 2020 can't use the Hub's
rem licensing service, and looks in the normal folder instead, so this copies your own licence file across.
rem Double-click it from File Explorer: other apps from the Store can have private folders of their own.
setlocal
set "HUB_LICENCE=%LOCALAPPDATA%\Packages\UnityTechnologies.UnityHub_2vrhnee42bhxm\LocalCache\Local\Unity\licenses\UnityEntitlementLicense.xml"
set "UNITY_FOLDER=%LOCALAPPDATA%\Unity"
set "LICENCES=%UNITY_FOLDER%\licenses"
set "REPORT=%~dp0licence-repair-report.txt"

> "%REPORT%" echo Repair run on %DATE% %TIME%
>> "%REPORT%" echo --- normal licence folder before
dir /a "%LICENCES%" >> "%REPORT%" 2>&1
>> "%REPORT%" echo --- Hub's licence
dir "%HUB_LICENCE%" >> "%REPORT%" 2>&1
>> "%REPORT%" echo --- end of the normal licensing log
powershell -NoProfile -Command "Get-Content -LiteralPath $env:UNITY_FOLDER'\Unity.Licensing.Client.log' -Tail 40 -ErrorAction SilentlyContinue" >> "%REPORT%" 2>&1

if not exist "%HUB_LICENCE%" (
    echo The Microsoft Store version of Unity Hub has no licence here, so there's nothing to copy.
    echo If you use that version, sign in to the Hub and add a licence under Preferences, Licenses, then run this again.
    >> "%REPORT%" echo RESULT: no Hub licence
    pause
    exit /b 1
)

if not exist "%LICENCES%" mkdir "%LICENCES%"
if exist "%LICENCES%\UnityEntitlementLicense.xml" copy /y "%LICENCES%\UnityEntitlementLicense.xml" "%LICENCES%\UnityEntitlementLicense.xml.bak" >nul
copy /y "%HUB_LICENCE%" "%LICENCES%\UnityEntitlementLicense.xml" >nul
fc /b "%HUB_LICENCE%" "%LICENCES%\UnityEntitlementLicense.xml" >nul
if errorlevel 1 (
    echo The licence couldn't be copied. Nothing else was changed.
    >> "%REPORT%" echo RESULT: copy failed
    pause
    exit /b 1
)

>> "%REPORT%" echo RESULT: copied
echo Done. Open your project from Unity Hub again.
echo The licence renews in the Hub about once a month. If this error comes back, open the Hub, then run this again.
pause
