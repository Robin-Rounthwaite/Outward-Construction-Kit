<#
.SYNOPSIS
Checks the game survives broken regions, as after a game update or a bad download: it installs some alongside a
damaged copy of test_alpha, runs the game, and puts everything back. Takes a few minutes and takes over the game window.

.PARAMETER Case
What's wrong with test_alpha: "assets" names things that aren't in the game, and "environment" names a sky and audio
scene that doesn't exist.
#>
param(
    [ValidateSet('assets', 'environment')][string]$Case = 'assets',
    [int]$TimeoutSeconds = 360
)

$ErrorActionPreference = 'Stop'
$paths = & "$PSScriptRoot\..\dev\dev-paths.ps1"
$bepinex = Join-Path $paths.Profile 'BepInEx'
$plugins = Join-Path $bepinex 'plugins'
$alpha = Join-Path $plugins 'OCK-Region-test_alpha\test_alpha.refs.txt'
$original = Get-Content $alpha -Raw
$broken = @('OCK-Region-broken_bundle', 'OCK-Region-broken_nobundle', 'OCK-Region-broken_manifest') | ForEach-Object { Join-Path $plugins $_ }

function Add-Broken($index, $bundle, $manifest) {
    New-Item -ItemType Directory -Force $broken[$index] | Out-Null
    $name = (Split-Path $broken[$index] -Leaf) -replace '^OCK-Region-', ''
    if ($bundle) { [IO.File]::WriteAllBytes((Join-Path $broken[$index] $name), [byte[]](1..200)) }
    Set-Content (Join-Path $broken[$index] "$name.refs.txt") $manifest -Encoding ASCII
}

try {
    Add-Broken 0 $true @('region | broken_bundle | Broken Bundle', 'environment | Monsoon')
    Add-Broken 1 $false @('region | broken_nobundle | Missing Bundle', 'environment | Monsoon')
    Add-Broken 2 $true @('nonsense', 'prop | too | few', '| | | |', 'region')

    $damage = if ($Case -eq 'assets') {
        @(
            'prop | Monsoon | Nowhere/Missing Prop | 1,0,1 | 0 | wrong:1',
            'enemy | Monsoon | Nowhere/Missing Enemy | 2,0,2 | 0',
            'ground | Monsoon | Ground_Grass | tex_no_such_texture |',
            'water | NoSuchScene | 5,0,5 | 10,1,10 | 0',
            'doormodel | Monsoon | Nowhere/Missing Door | 0,0,0 | 1'
        )
    } else {
        @()
    }
    $text = ($original -replace '(?m)^game \|.*$', 'game | 0000000000000000000000000000000000000000').TrimEnd()
    if ($Case -eq 'environment') { $text = $text -replace '(?m)^environment \|.*$', 'environment | NoSuchScene' }
    Set-Content $alpha (($text, ($damage -join "`n")) -join "`n") -Encoding ASCII -NoNewline

    powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'run-game-test.ps1') -Regions 'test_gamma,test_alpha' -TimeoutSeconds $TimeoutSeconds
    "Runner exit code $LASTEXITCODE"
    Select-String -Path (Join-Path $bepinex 'LogOutput.log') -Pattern '\[Error|\[Warning:Outward|Notice' | ForEach-Object { $_.Line }
}
finally {
    Set-Content $alpha $original -Encoding ASCII -NoNewline
    $broken | Where-Object { Test-Path $_ } | ForEach-Object { Remove-Item $_ -Recurse -Force -Confirm:$false }
    'Everything put back.'
}
