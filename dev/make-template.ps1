# Rebuilds src/OCK.Extractor/template/ock_template with Unity. Needed only when the extractor reports a class the
# template can't describe, after adding that class to template-maker/Editor/OckTemplateMaker.cs.
# Opens the project for a moment, so it must not already be open.
param(
    [string]$Project = (& "$PSScriptRoot\dev-paths.ps1").RegionProject,
    [string]$Unity = "C:\Program Files\Unity 2020.3.26f1\Editor\Unity.exe",
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = "Stop"
if (Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -match [regex]::Escape((Split-Path $Project -Leaf)) }) {
    throw "Unity has $Project open. Close it first."
}

$assets = Join-Path $Project "Assets\_OckTemplateMaker"
$output = Join-Path ([IO.Path]::GetTempPath()) ("ock-template-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $output | Out-Null
Copy-Item (Join-Path $PSScriptRoot "template-maker") $assets -Recurse

$env:OCK_TEMPLATE_OUT = $output
$log = Join-Path $output "unity.txt"
$unityProcess = Start-Process $Unity -ArgumentList "-projectPath `"$Project`" -logFile `"$log`"" -PassThru
try {
    # A compile error leaves the editor sitting open, with nothing to wait for.
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (-not $unityProcess.WaitForExit(3000)) {
        $broken = (Test-Path $log) -and (Select-String -Path $log -Pattern "_OckTemplateMaker.*error CS" -Quiet)
        if ($broken -or $timer.Elapsed.TotalSeconds -gt $TimeoutSeconds) {
            Stop-Process -Id $unityProcess.Id -Force -Confirm:$false
            $unityProcess.WaitForExit(30000) | Out-Null
            if ($broken) { Select-String -Path $log -Pattern "error CS" | Select-Object -First 5 | ForEach-Object { $_.Line } }
            throw "Unity didn't build the template. Its log is $log."
        }
    }
}
finally {
    if (Test-Path $assets) { Remove-Item $assets -Recurse -Force -Confirm:$false }
    if (Test-Path "$assets.meta") { Remove-Item "$assets.meta" -Force -Confirm:$false }
    Remove-Item (Join-Path $Project "Temp\UnityLockfile") -Force -Confirm:$false -ErrorAction SilentlyContinue
}

if (Test-Path (Join-Path $output "failed.txt")) { Get-Content (Join-Path $output "failed.txt") -TotalCount 8 }
$built = Join-Path $output "ock_template"
if (-not (Test-Path $built)) { throw "No template was built. Unity's log is $log." }
Copy-Item $built (Join-Path $PSScriptRoot "..\src\OCK.Extractor\template\ock_template") -Force
"Template rebuilt, $((Get-Item $built).Length) bytes."
