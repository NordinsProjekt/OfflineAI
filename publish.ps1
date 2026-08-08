<#
.SYNOPSIS
    Publishes AiDashboard to its configured Desktop folder.

.DESCRIPTION
    Wraps `dotnet publish` using AiDashboard's existing publish profile
    (AiDashboard/Properties/PublishProfiles/FolderProfile.pubxml), which already points at
    C:\Users\<user>\Desktop\OfflineAIDashboard and is configured self-contained win-x64 (required
    because Microsoft.ML.OnnxRuntime ships no win-x86 native assets, so an x86 publish would
    silently pick up whatever onnxruntime.dll happens to be on the target machine).

.PARAMETER Configuration
    Build configuration to publish. Defaults to Release.

.PARAMETER Open
    Opens the publish output folder in Explorer after a successful publish.

.EXAMPLE
    ./publish.ps1

.EXAMPLE
    ./publish.ps1 -Open
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$Open
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot "AiDashboard\AiDashboard.csproj"
$profileName = "FolderProfile"
$profileFile = Join-Path $repoRoot "AiDashboard\Properties\PublishProfiles\$profileName.pubxml"

if (-not (Test-Path $project)) {
    throw "Could not find project at $project"
}
if (-not (Test-Path $profileFile)) {
    throw "Could not find publish profile at $profileFile"
}

# Read the folder the profile publishes to, purely to report/open it afterwards -- the profile
# itself (not this script) is the source of truth for configuration/self-contained/RID/output path.
[xml]$profileXml = Get-Content $profileFile
$publishUrl = $profileXml.Project.PropertyGroup.PublishUrl

Write-Host "Publishing AiDashboard ($Configuration) to $publishUrl ..." -ForegroundColor Cyan

# `-p:PublishProfile=` alone does not reliably honor the profile's <PublishUrl> with this SDK --
# it silently falls back to the default bin\<config>\<tfm>\<rid>\publish\ folder instead. Passing
# -o explicitly forces the real output location while the profile still supplies everything else
# (self-contained, RID, DeleteExistingFiles).
dotnet publish $project -c $Configuration -p:PublishProfile=$profileName -o $publishUrl
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Publish succeeded: $publishUrl" -ForegroundColor Green

if ($Open) {
    Invoke-Item $publishUrl
}
