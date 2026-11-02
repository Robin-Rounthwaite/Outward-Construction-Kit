<#
.SYNOPSIS
Rips one scene from your own copy of the game with AssetRipper, for showing its models in the Prop Browser. Installs
AssetRipper first if needed. Reads the game's files without changing them.

.PARAMETER GameData
The game's "Outward Definitive Edition_Data" folder.

.PARAMETER Level
The scene's build number: its level file's number, so 52 for Monsoon.

.PARAMETER Scene
The scene's name, which names the folder its rip goes in.

.PARAMETER RipFolder
The folder holding your rips. AssetRipper is installed inside it too.

.PARAMETER ToolFolder
An AssetRipper already installed, to use instead.

.PARAMETER NoPause
Closes at the end instead of waiting for a key, for running from another script.
#>
param(
    [Parameter(Mandatory = $true)][string]$GameData,
    [Parameter(Mandatory = $true)][int]$Level,
    [Parameter(Mandatory = $true)][string]$Scene,
    [Parameter(Mandatory = $true)][string]$RipFolder,
    [string]$ToolFolder,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue' # which makes downloads far quicker

# Pinned, so everyone's rips come out the same, and checked against the hash its release published.
$RIPPER_VERSION = '2.0.0'
$RIPPER_URL = "https://github.com/AssetRipper/AssetRipper/releases/download/$RIPPER_VERSION/AssetRipper_win_x64.zip"
$RIPPER_SHA256 = '9a7ef0e7c5c3ea5b90b4e6d855e2d98d5f7ec8c3f9e26fccbc194c6a7b01baf7'
$START_TIMEOUT_SECONDS = 120
$WORK_TIMEOUT_SECONDS = 7200
$STALLED_SECONDS = 900

function Install-Ripper($folder) {
    Write-Host "Downloading AssetRipper $RIPPER_VERSION from GitHub, about 42 MB..."
    New-Item -ItemType Directory -Force $folder | Out-Null
    $zip = Join-Path $folder 'AssetRipper.zip'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest $RIPPER_URL -OutFile $zip -UseBasicParsing
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash
    if ($hash -ne $RIPPER_SHA256) {
        Remove-Item $zip -Force
        throw "The download doesn't match the published AssetRipper $RIPPER_VERSION, so it wasn't used."
    }
    Expand-Archive $zip $folder -Force
    Remove-Item $zip -Force
}

function Get-FreePort {
    $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    return $port
}

$levelFile = Join-Path $GameData "level$Level"
if (-not (Test-Path $levelFile)) { throw "There's no $levelFile. Check the game data folder." }

$output = Join-Path $RipFolder $Scene
if (Test-Path $output) { throw "$output already exists. Delete it first to rip this scene again." }

$ripperHome = Join-Path $RipFolder '_AssetRipper'
$ownCopy = -not $ToolFolder
if ($ownCopy) { $ToolFolder = Join-Path $ripperHome $RIPPER_VERSION }
$exe = Get-ChildItem $ToolFolder -Recurse -Filter 'AssetRipper*.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $exe -and -not $ownCopy) { throw "There's no AssetRipper program in $ToolFolder. Check the folder in Construction Kit > Paths, or empty it to use the kit's own." }
if ($exe -and -not $ownCopy -and $exe.VersionInfo.ProductVersion -notlike "$RIPPER_VERSION*") {
    Write-Host "This AssetRipper is version $($exe.VersionInfo.ProductVersion), and the kit is tested with $RIPPER_VERSION, so its rips may not read correctly."
}
if (-not $exe) {
    Install-Ripper $ToolFolder
    $exe = Get-ChildItem $ToolFolder -Recurse -Filter 'AssetRipper*.exe' | Select-Object -First 1
    if (-not $exe) { throw "AssetRipper's download had no program in it." }
}

# Built here and moved at the end, so a rip that's stopped partway never looks like a finished one.
$work = Join-Path $ripperHome "work\$Scene"
if (Test-Path $work) { Remove-Item $work -Recurse -Force -Confirm:$false }
New-Item -ItemType Directory -Force (Split-Path $work) | Out-Null
$logFile = Join-Path (Split-Path $work) "$Scene.log"

$port = Get-FreePort
$timer = [Diagnostics.Stopwatch]::StartNew()
$ripper = Start-Process $exe.FullName -ArgumentList "--headless --port $port --log-path `"$logFile`"" -PassThru -WindowStyle Hidden
try {
    $base = "http://127.0.0.1:$port"
    $up = $false
    while (-not $up) {
        if ($timer.Elapsed.TotalSeconds -gt $START_TIMEOUT_SECONDS) { throw "AssetRipper didn't start. Its log is $logFile." }
        Start-Sleep 2
        try {
            Invoke-WebRequest "$base/" -UseBasicParsing -TimeoutSec 5 | Out-Null
            $up = $true
        }
        catch { }
    }

    $post = {
        param($url, $body)
        # A finished request redirects, which counts as an error here but isn't one.
        try {
            Invoke-WebRequest "$base$url" -Method Post -ContentType 'application/x-www-form-urlencoded' -Body $body -UseBasicParsing -DisableKeepAlive -TimeoutSec $WORK_TIMEOUT_SECONDS -MaximumRedirection 0 -ErrorAction Stop | Out-Null
        }
        catch {
            if ($_.FullyQualifiedErrorId -notmatch 'MaximumRedirectExceeded') { throw }
        }
    }
    $encode = { param($s) [uri]::EscapeDataString($s) }

    Write-Host "Reading $Scene from the game's files. This takes a few minutes..."
    $body = 'path=' + (& $encode $levelFile)
    $shared = Join-Path $GameData "sharedassets$Level.assets"
    if (Test-Path $shared) { $body += '&path=' + (& $encode $shared) }
    & $post '/LoadFile' $body

    Write-Host "Exporting $Scene. This can take a while, and a lot of disk space..."

    # Its log says how this is going. The request is only what starts it, because one held open for this long can
    # drop while the export carries on perfectly well.
    $request = [Net.HttpWebRequest][Net.WebRequest]::Create("$base/Export/UnityProject")
    $request.Method = 'POST'
    $request.ContentType = 'application/x-www-form-urlencoded'
    $request.AllowAutoRedirect = $false
    $request.KeepAlive = $false
    $request.Timeout = [Threading.Timeout]::Infinite
    $bytes = [Text.Encoding]::ASCII.GetBytes('path=' + (& $encode $work))
    $stream = $request.GetRequestStream()
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Close()
    [void]$request.BeginGetResponse($null, $null)

    $lastChange = [Diagnostics.Stopwatch]::StartNew()
    $lastLine = ''
    $finished = $false
    while (-not $finished) {
        Start-Sleep 5
        if ($ripper.HasExited) { throw "AssetRipper closed partway through. Its log is $logFile." }
        $line = Get-Content $logFile -Tail 1 -ErrorAction SilentlyContinue
        if ($line -ne $lastLine) {
            $lastLine = $line
            $lastChange.Restart()
            if ($line -match '\((\d+)/(\d+)\)') { Write-Host ("  {0:0}%" -f (100 * [int]$Matches[1] / [int]$Matches[2])) }
        }
        $finished = $line -match 'Finished post-export'
        if (-not $finished -and $lastChange.Elapsed.TotalSeconds -gt $STALLED_SECONDS) { throw "AssetRipper has stopped making progress. Its log is $logFile." }
    }
}
finally {
    Stop-Process -Id $ripper.Id -Force -Confirm:$false -ErrorAction SilentlyContinue
}

if (-not (Test-Path (Join-Path $work "ExportedProject\Assets\Scenes\level$Level.unity"))) {
    throw "AssetRipper finished, but $Scene's scene isn't in what it made. Its log is $logFile."
}
Start-Sleep 2 # for it to let go of its files
Move-Item $work $output

$size = (Get-ChildItem $output -Recurse -File | Measure-Object Length -Sum).Sum / 1GB
try {
    & (Join-Path $PSScriptRoot 'share-rip-files.ps1') -RipFolder $RipFolder -Scene $Scene
}
catch {
    # The rip itself is fine, it just takes more space.
    Write-Host "Couldn't share files with your other rips: $_"
}
Write-Host ("Done: $output, {0:0.0} GB before sharing, in {1:0} minutes. It appears in the Prop Browser by itself." -f $size, $timer.Elapsed.TotalMinutes)
if (-not $NoPause) { Read-Host 'Press Enter to close' | Out-Null }
