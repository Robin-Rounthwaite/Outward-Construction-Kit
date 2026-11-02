<#
.SYNOPSIS
Rips a list of scenes one after another, using rip-scene.ps1. One that fails doesn't stop the rest, a scene already
ripped is skipped, and it stops before filling the drive. Running it again carries on with what's left.

.PARAMETER GameData
The game's "Outward Definitive Edition_Data" folder.

.PARAMETER Scenes
The scenes to rip, as build number=name, separated by commas: 5=ChersoneseNewTerrain,52=Monsoon

.PARAMETER RipFolder
The folder holding your rips. AssetRipper is installed inside it too.

.PARAMETER MinFreeGB
Stops before starting another scene if the drive has less than this free, since one scene can take about 15 GB.

.PARAMETER ToolFolder
An AssetRipper already installed, to use instead.

.PARAMETER NoPause
Closes at the end instead of waiting for a key, for running from another script.
#>
param(
    [Parameter(Mandatory = $true)][string]$GameData,
    [Parameter(Mandatory = $true)][string]$Scenes,
    [Parameter(Mandatory = $true)][string]$RipFolder,
    [int]$MinFreeGB = 20,
    [string]$ToolFolder,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'

function Get-FreeGB($folder) {
    New-Item -ItemType Directory -Force $folder | Out-Null
    $root = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($folder))
    return [math]::Floor((New-Object IO.DriveInfo($root)).AvailableFreeSpace / 1GB)
}

$list = @($Scenes -split ',' | Where-Object { $_ })
$ripped = 0
$skipped = 0
$failed = @()
$stoppedAt = $null
$timer = [Diagnostics.Stopwatch]::StartNew()

for ($i = 0; $i -lt $list.Count; $i++) {
    $level, $scene = $list[$i] -split '=', 2
    if (Test-Path (Join-Path $RipFolder $scene)) {
        $skipped++
        continue
    }
    $free = Get-FreeGB $RipFolder
    if ($free -lt $MinFreeGB) {
        $stoppedAt = "Stopped before $scene, with $free GB free on the drive."
        break
    }

    Write-Host ''
    Write-Host ("=== Scene {0} of {1}: {2}, {3} GB free, {4:0.0} hours so far ===" -f ($i + 1), $list.Count, $scene, $free, $timer.Elapsed.TotalHours)
    $arguments = @{ GameData = $GameData; Level = [int]$level; Scene = $scene; RipFolder = $RipFolder; NoPause = $true }
    if ($ToolFolder) { $arguments.ToolFolder = $ToolFolder }
    try {
        & (Join-Path $PSScriptRoot 'rip-scene.ps1') @arguments
        $ripped++
    }
    catch {
        Write-Host "$scene failed: $_"
        $failed += $scene
    }
}

Write-Host ''
Write-Host ("Finished: {0} ripped, {1} already there, {2} failed, in {3:0.0} hours." -f $ripped, $skipped, $failed.Count, $timer.Elapsed.TotalHours)
if ($stoppedAt) { Write-Host "$stoppedAt Free up space, then run this again to carry on." }
if ($failed.Count -gt 0) { Write-Host "These failed, and running this again retries them: $($failed -join ', ')" }
Write-Host 'They appear in the Prop Browser by themselves.'
if (-not $NoPause) { Read-Host 'Press Enter to close' | Out-Null }
if ($stoppedAt -or $failed.Count -gt 0) { exit 1 }
