<#
.SYNOPSIS
Runs the game with no one at the keyboard: loads the named character, visits each named region, checks it, comes
back and quits. Takes a few minutes and takes over the PC's game window. Saves are backed up first.

.PARAMETER Regions
The IDs of the regions to visit.

.PARAMETER Stay
Leaves the game open at the end, for a person to take over.

.PARAMETER TimeoutSeconds
How long to wait before stopping the game and reporting that it hung.
#>
param(
    [Parameter(Mandatory = $true)][string[]]$Regions,
    [switch]$Stay,
    [string]$Character = 'Jim of the Doors',
    [int]$TimeoutSeconds = 600,
    [int]$AppId = 1758860,
    [string]$GameFolder = 'T:\SteamLibrary\steamapps\common\Outward\Outward_Defed',
    [string]$Profile = "$env:APPDATA\r2modmanPlus-local\OutwardDe\profiles\modding-experiments"
)

$bepinex = Join-Path $Profile 'BepInEx'
$log = Join-Path $bepinex 'LogOutput.log'

if (Get-Process | Where-Object { $_.ProcessName -match '^Outward' }) {
    Write-Error 'The game is already running. Close it first.'
    exit 2
}
if (-not (Get-Process steam -ErrorAction SilentlyContinue)) {
    Write-Error 'Steam needs to be running and signed in.'
    exit 2
}

$backup = Join-Path $env:TEMP 'ock-save-backup'
robocopy (Join-Path $GameFolder 'SaveGames') $backup /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
Write-Host "Saves backed up to $backup"

$request = @("character $Character") + @($Regions -split ',' | ForEach-Object { "region $($_.Trim())" })
if ($Stay) { $request += 'stay' }
Set-Content -Path (Join-Path $bepinex 'OCK_AutoTest.txt') -Value $request -Encoding ASCII

# How r2modman starts a modded game: through Steam, with the Definitive Edition's ID, pointed at the profile's BepInEx.
$steam = (Get-Process steam | Select-Object -First 1).Path
$target = Join-Path $bepinex 'core\BepInEx.Preloader.dll'
$started = Get-Date
Start-Process $steam -ArgumentList "-applaunch $AppId --doorstop-enable true --doorstop-target `"$target`""

$game = $null
while (((Get-Date) - $started).TotalSeconds -lt 120 -and $null -eq $game) {
    Start-Sleep 2
    $game = Get-Process | Where-Object { $_.ProcessName -match '^Outward' } | Select-Object -First 1
}
if ($null -eq $game) {
    Write-Host 'The game never started. Steam may be showing a prompt.'
    Remove-Item -LiteralPath (Join-Path $bepinex 'OCK_AutoTest.txt') -Force -Confirm:$false -ErrorAction SilentlyContinue
    exit 1
}
Write-Host "Started the game, process $($game.Id)."

$finished = $false
while (((Get-Date) - $started).TotalSeconds -lt $TimeoutSeconds) {
    Start-Sleep 3
    # The log is replaced as the game starts, so an old one mustn't count.
    if ((Get-Item $log).LastWriteTime -gt $started -and (Select-String -Path $log -Pattern '\[AUTOTEST\] FINISHED|\[AUTOTEST\] FAIL' -Quiet -ErrorAction SilentlyContinue)) {
        $finished = $true
        break
    }
    if ($game.HasExited) {
        # It may have logged its last line just before closing.
        $finished = Select-String -Path $log -Pattern '\[AUTOTEST\] FINISHED|\[AUTOTEST\] FAIL' -Quiet
        break
    }
}
$seconds = [int]((Get-Date) - $started).TotalSeconds

if (-not $Stay -and -not $game.HasExited) {
    # A normal close saves, so it gets a moment before being stopped.
    if (-not $game.WaitForExit(30000)) {
        Stop-Process -Id $game.Id -Force -Confirm:$false
        Write-Host 'Had to stop the game, because it did not close by itself.'
    }
}

Write-Host "--- after $seconds s, finished: $finished, game still running: $(-not $game.HasExited)"
Select-String -Path $log -Pattern '\[AUTOTEST\]|\[CHECK\]|Asset cache|Extraction finished|Donor .*Memory|cached|Exception' | ForEach-Object { $_.Line -replace '^\[\w+ *:Outward Construction Kit\] ', '' }

if (-not $finished) {
    Write-Host 'The test did not finish.'
    exit 1
}
if (Select-String -Path $log -Pattern '\[AUTOTEST\] FAIL|\[CHECK\] FAIL' -Quiet) {
    Write-Host 'The test finished, with failures.'
    exit 1
}
Write-Host 'The test passed.'
