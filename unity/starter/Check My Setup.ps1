<#
.SYNOPSIS
Looks at this PC and says what's still needed for making regions. It only reads, and changes nothing.
#>
$UNITY_VERSION = '2020.3.26f1'
$UNITY_INSTALL_LINK = 'unityhub://2020.3.26f1/7298b473bc1a'
$GAME_DATA = 'Outward\Outward_Defed\Outward Definitive Edition_Data'
$MONSOON_RIP_GB = 15
$LONG_PATH = 60

$script:problems = 0
function Ok($text) { Write-Host "  OK    $text" -ForegroundColor Green }
function Note($text) { Write-Host "  NOTE  $text" -ForegroundColor Yellow }
function Fix($text) {
    Write-Host "  FIX   $text" -ForegroundColor Red
    $script:problems++
}

Write-Host "`nUnity"
$hubFolders = @("$env:ProgramFiles\Unity\Hub\Editor")
$secondary = Join-Path $env:APPDATA 'UnityHub\secondaryInstallPath.json'
if (Test-Path $secondary) { $hubFolders += (Get-Content $secondary -Raw | ConvertFrom-Json) }
$unity = @($hubFolders + $env:ProgramFiles | Where-Object { $_ } | ForEach-Object {
    Join-Path $_ "$UNITY_VERSION\Editor\Unity.exe"
    Join-Path $_ "Unity $UNITY_VERSION\Editor\Unity.exe"
} | Where-Object { Test-Path $_ }) | Select-Object -First 1
if ($unity) { Ok "Unity $UNITY_VERSION is at $unity" }
else { Fix "Unity $UNITY_VERSION wasn't found. With the Hub installed, paste this into your browser: $UNITY_INSTALL_LINK" }

$storeHub = Get-AppxPackage -Name 'UnityTechnologies.UnityHub' -ErrorAction SilentlyContinue
$privateLicence = Join-Path $env:LOCALAPPDATA 'Packages\UnityTechnologies.UnityHub_2vrhnee42bhxm\LocalCache\Local\Unity\licenses\UnityEntitlementLicense.xml'
$normalLicence = Join-Path $env:LOCALAPPDATA 'Unity\licenses\UnityEntitlementLicense.xml'
if ($storeHub) {
    Ok 'Unity Hub is the Microsoft Store version.'
    if (-not (Test-Path $privateLicence)) { Fix 'The Hub has no licence yet. Sign in, and add one under Preferences, Licenses.' }
    elseif (-not (Test-Path $normalLicence)) { Fix 'Unity 2020 will not find your licence. Double-click Repair Unity Licence.cmd in this folder.' }
    else { Ok 'Your licence is where Unity 2020 looks. If Unity still closes by itself, it may be out of date: run Repair Unity Licence.cmd again.' }
}
elseif ((Test-Path "$env:ProgramFiles\Unity Hub\Unity Hub.exe") -or (Test-Path $normalLicence)) {
    Ok 'Unity Hub is the version from unity.com.'
    if (-not (Test-Path $normalLicence)) { Fix 'No licence was found. Sign in to the Hub, and add one under Preferences, Licenses.' }
}
else { Fix 'Unity Hub was not found. Install it from https://unity.com/download' }

Write-Host "`nThe game"
$steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
$libraries = @()
if ($steam) {
    $libraries += $steam
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) { $libraries += Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' | ForEach-Object { $_.Matches[0].Groups[1].Value -replace '\\\\', '\' } }
}
$game = $libraries | Select-Object -Unique | ForEach-Object { Join-Path $_ "steamapps\common\$GAME_DATA" } | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($game) { Ok "Outward: Definitive Edition is at $(Split-Path $game)" }
else { Fix 'Outward: Definitive Edition was not found in your Steam libraries. It is needed, because everything comes from your own copy.' }

Write-Host "`nr2modman"
$profiles = Join-Path $env:APPDATA 'r2modmanPlus-local\OutwardDe\profiles'
$kit = @()
if (Test-Path $profiles) {
    $kit = @(Get-ChildItem $profiles -Directory | Where-Object { Get-ChildItem (Join-Path $_.FullName 'BepInEx\plugins') -Recurse -Filter 'OutwardConstructionKit.dll' -ErrorAction SilentlyContinue })
    if ($kit) { Ok "The kit is in these profiles: $($kit.Name -join ', ')" }
    else { Fix 'No r2modman profile has the kit yet. Install Outward_Construction_Kit into one.' }
}
else { Fix 'r2modman has no Outward: Definitive Edition profile yet. Install it from https://thunderstore.io/package/ebkr/r2modman/ and create one.' }

Write-Host "`nThis project"
$project = $PSScriptRoot
if ($project -match 'OneDrive|Dropbox|Google Drive') { Fix "This project is in a synced folder, which breaks exports. Move it elsewhere: $project" }
elseif ($project.Length -gt $LONG_PATH) { Note "This project's path is long, and a copy under a very long path sat idle after opening in testing. A short one is safer: $project" }
else { Ok "This project is in a good place: $project" }
$free = [math]::Floor((New-Object IO.DriveInfo([IO.Path]::GetPathRoot($project))).AvailableFreeSpace / 1GB)
if ($free -lt $MONSOON_RIP_GB * 2) { Note "This drive has $free GB free. Your first rip needs about $MONSOON_RIP_GB GB, and can go on another drive." }
else { Ok "This drive has $free GB free. Your first rip needs about $MONSOON_RIP_GB GB." }

Write-Host ''
if ($script:problems -eq 0) { Write-Host 'Nothing is missing. Open this project from Unity Hub.' -ForegroundColor Green }
else { Write-Host "$script:problems thing(s) to fix. Guides\getting-started.md has the details." -ForegroundColor Yellow }
Write-Host ''
