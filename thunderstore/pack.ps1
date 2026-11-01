# Builds the Thunderstore upload zip for the Construction Kit's plugin.
#
# Usage (from anywhere):   powershell -ExecutionPolicy Bypass -File thunderstore\pack.ps1
#
# For a new release:
#   1. Bump "version_number" in thunderstore\manifest.json
#   2. Bump VERSION in src\OCK.Runtime\KitPlugin.cs and "version" in the Unity package's package.json to the same number
#   3. Add an entry to thunderstore\CHANGELOG.md
#   4. Run this script, then upload thunderstore\dist\<name>-<version>.zip
#
# Region mods depend on the version in package.json, so it needs to be on Thunderstore before any region built with it.

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Split-Path -Parent $here
$runtime = Join-Path $repo 'src\OCK.Runtime'

$manifest = Get-Content (Join-Path $here 'manifest.json') -Raw | ConvertFrom-Json
$version = $manifest.version_number

$plugin = Get-Content (Join-Path $runtime 'KitPlugin.cs') -Raw
if ($plugin -notmatch 'VERSION\s*=\s*"([^"]+)"') { throw 'Could not find VERSION in KitPlugin.cs' }
if ($Matches[1] -ne $version) { throw "manifest.json says $version but KitPlugin.cs says $($Matches[1]). Make them match first." }
$package = Get-Content (Join-Path $repo 'unity\com.outwardconstructionkit.editor\package.json') -Raw | ConvertFrom-Json
if ($package.version -ne $version) { throw "manifest.json says $version but package.json says $($package.version). Make them match first." }
if ($manifest.description.Length -gt 250) { throw "The description is $($manifest.description.Length) characters, and Thunderstore allows 250." }
if (-not (Select-String -Path (Join-Path $here 'CHANGELOG.md') -Pattern ('^##\s+' + [regex]::Escape($version) + '\s*$') -Quiet)) { throw "CHANGELOG.md has no '## $version' entry." }

dotnet build (Join-Path $runtime 'OCK.Runtime.csproj') -c Release --nologo -v q | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

Add-Type -AssemblyName System.Drawing
$image = [Drawing.Image]::FromFile((Join-Path $here 'icon.png'))
$size = "$($image.Width)x$($image.Height)"
$image.Dispose()
if ($size -ne '256x256') { throw "icon.png must be 256x256 (it is $size)" }

$stage = Join-Path $here 'dist\_stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force -Confirm:$false }
New-Item -ItemType Directory $stage | Out-Null
$built = Join-Path $runtime 'bin\Release'
foreach ($dll in 'OutwardConstructionKit.dll', 'OutwardConstructionKit.Extractor.dll', 'AssetsTools.NET.dll', 'AssetsTools.NET.MonoCecil.dll') {
    Copy-Item (Join-Path $built $dll) $stage
}
foreach ($file in 'manifest.json', 'README.md', 'CHANGELOG.md', 'icon.png', 'THIRD-PARTY-NOTICES.txt') {
    Copy-Item (Join-Path $here $file) $stage
}
Copy-Item (Join-Path $repo 'LICENSE') $stage

$zip = Join-Path $here "dist\$($manifest.name)-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force -Confirm:$false }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
Remove-Item $stage -Recurse -Force -Confirm:$false

Write-Host "Created $zip, $([int]((Get-Item $zip).Length / 1KB)) KB." -ForegroundColor Green
