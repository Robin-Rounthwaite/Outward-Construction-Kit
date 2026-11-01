<#
.SYNOPSIS
Builds the starter project zip that modders download: a Unity project with the kit inside, needing no Git and no
editing of files. It holds only the kit's own files and project settings, no game files.

.PARAMETER Output
The folder to write the zip to.
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\dist')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repo = Split-Path $PSScriptRoot
$package = Join-Path $repo 'unity\com.outwardconstructionkit.editor'
$template = Join-Path $repo 'unity\RegionProjectTemplate'
$version = (Get-Content (Join-Path $package 'package.json') -Raw | ConvertFrom-Json).version
$name = "Outward_Construction_Kit_Starter-$version"

$work = Join-Path ([IO.Path]::GetTempPath()) ('ock-starter-' + [guid]::NewGuid().ToString('N'))
$project = Join-Path $work 'OutwardRegionProject'
try {
    New-Item -ItemType Directory -Force (Join-Path $project 'Packages') | Out-Null
    New-Item -ItemType Directory -Force (Join-Path $project 'Assets') | Out-Null
    New-Item -ItemType Directory -Force (Join-Path $project 'Guides') | Out-Null
    Copy-Item (Join-Path $template 'ProjectSettings') $project -Recurse
    Copy-Item $package (Join-Path $project 'Packages\com.outwardconstructionkit.editor') -Recurse

    # A package inside Packages is found by itself, so the template's link to it goes.
    $manifest = Get-Content (Join-Path $template 'Packages\manifest.json')
    $kept = @($manifest | Where-Object { $_ -notmatch 'com\.outwardconstructionkit\.editor' })
    if ($kept.Count -ne $manifest.Count - 1) { throw "Expected the manifest to name the kit once." }
    [IO.File]::WriteAllLines((Join-Path $project 'Packages\manifest.json'), [string[]]$kept)

    Copy-Item (Join-Path $repo 'docs\getting-started.md'), (Join-Path $repo 'docs\tutorial.md'), (Join-Path $repo 'docs\making-a-region.md') (Join-Path $project 'Guides')
    Copy-Item (Join-Path $repo 'LICENSE') $project
    Copy-Item (Join-Path $repo 'unity\starter\*') $project -Exclude 'licence-repair-report.txt' # it holds that PC's paths
    [IO.File]::WriteAllText((Join-Path $project 'START HERE.txt'), "Open Guides\getting-started.md and follow it.`r`n")

    $bad = Get-ChildItem $project -Recurse -File | Where-Object { $_.Extension -in '.assets', '.resS', '.bundle' -or $_.Name -like 'level*' }
    if ($bad) { throw "Game files must never ship: $($bad.FullName -join ', ')" }

    New-Item -ItemType Directory -Force $Output | Out-Null
    $zip = Join-Path (Resolve-Path $Output) "$name.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force -Confirm:$false }
    [IO.Compression.ZipFile]::CreateFromDirectory($work, $zip)
    "Built $zip, $([int]((Get-Item $zip).Length / 1KB)) KB."
}
finally {
    Remove-Item $work -Recurse -Force -Confirm:$false -ErrorAction SilentlyContinue
}
