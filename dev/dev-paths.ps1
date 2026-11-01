# Reads your paths from Directory.Build.user.props, for the scripts that work on the kit itself.
# Copy Directory.Build.user.props.example to that name, at the top of the repository, and fill it in.
$file = Join-Path $PSScriptRoot '..\Directory.Build.user.props'
if (-not (Test-Path $file)) {
    throw "No $file yet. Copy Directory.Build.user.props.example to that name and fill it in."
}
$settings = ([xml](Get-Content $file -Raw)).Project.PropertyGroup
[pscustomobject]@{
    GameData      = Split-Path $settings.OutwardManaged
    Profile       = $settings.ProfileDir
    RegionProject = $settings.OckRegionProject
    RipFolder     = $settings.OckRipFolder
}
