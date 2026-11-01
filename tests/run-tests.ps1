<#
.SYNOPSIS
Runs the kit's automated tests. With no switches, runs the fast tests of the plugin's logic, which take seconds.

.PARAMETER Unity
Also runs the Unity suite on the region project, then checks the plugin can read what it exported. Takes minutes,
and Unity must be closed first. It opens a window, because batch mode needs a licence activated for it.

.PARAMETER UnityProject
The region project to test.

.PARAMETER UnityEditor
The editor to run it with, which must be the game's Unity version.

Paths default to those in Directory.Build.user.props, as dev/dev-paths.ps1 explains.
#>
param(
    [switch]$Unity,
    [string]$UnityProject = (& "$PSScriptRoot\..\dev\dev-paths.ps1").RegionProject,
    [string]$UnityEditor = 'C:\Program Files\Unity 2020.3.26f1\Editor\Unity.exe'
)

$failed = $false

if ($Unity) {
    if (Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -match [regex]::Escape($UnityProject) }) {
        Write-Error 'Unity has the region project open. Close it first.'
        exit 2
    }
    $results = Join-Path ([IO.Path]::GetTempPath()) ('ock-tests-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $results | Out-Null
    $env:OCK_TEST_OUT = $results
    $paths = & "$PSScriptRoot\..\dev\dev-paths.ps1"
    $env:OCK_TEST_RIP_FOLDER = $paths.RipFolder
    $env:OCK_TEST_GAME_DATA = $paths.GameData
    Write-Host "Running the Unity suite. Results go to $results"
    $editor = Start-Process $UnityEditor -ArgumentList "-projectPath `"$UnityProject`" -ockSelfTest -logFile `"$results\unity.log`"" -PassThru
    $editor.WaitForExit()
    if (Test-Path "$results\selftest.txt") {
        Get-Content "$results\selftest.txt"
    } else {
        Write-Host "FAIL Unity wrote no results. See $results\unity.log"
    }
    if ($editor.ExitCode -ne 0) { $failed = $true }
    $env:OCK_TEST_MANIFESTS = $results
}

# Windows Defender can briefly lock a file the build is writing, especially just after Unity has closed.
for ($attempt = 1; $attempt -le 3; $attempt++) {
    $output = dotnet test (Join-Path $PSScriptRoot 'OCK.Runtime.Tests\OCK.Runtime.Tests.csproj') -c Release --nologo
    $code = $LASTEXITCODE
    $output | Write-Host
    if ($code -eq 0 -or -not ($output -match 'CS2012|being used by another process|cannot access the file')) { break }
    Write-Host "A build file was locked. Trying again, attempt $($attempt + 1)."
    Start-Sleep 15
}
if ($code -ne 0) { $failed = $true }

if ($failed) {
    Write-Host 'Some tests FAILED.'
    exit 1
}
Write-Host 'All tests passed.'
