[CmdletBinding()]
param(
    [string]$Artifact
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $Artifact) {
    $Artifact = "dist\Tech-Browser-$(& (Join-Path $PSScriptRoot 'version.ps1'))-x64-gdi.exe"
}
$distPath = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'dist')).TrimEnd('\')
$artifactPath = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $Artifact))
$verifyPath = [System.IO.Path]::GetFullPath((Join-Path $distPath 'verify-gdi-exe'))
$extractPath = Join-Path $verifyPath 'extract'
$smokeImagePath = [System.IO.Path]::GetFullPath((Join-Path $distPath 'gdi-smoke.png'))

foreach ($candidate in @($artifactPath, $verifyPath, $smokeImagePath)) {
    if (-not $candidate.StartsWith("$distPath\", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Verification path is outside dist: $candidate"
    }
}
if (-not (Test-Path -LiteralPath $artifactPath)) {
    throw "Artifact not found: $artifactPath"
}

if (Test-Path -LiteralPath $verifyPath) {
    Remove-Item -LiteralPath $verifyPath -Recurse -Force
}
if (Test-Path -LiteralPath $smokeImagePath) {
    Remove-Item -LiteralPath $smokeImagePath -Force
}
New-Item -ItemType Directory -Path $verifyPath | Out-Null

# Run a copy from an otherwise empty folder, the way the ScreenConnect toolbox starts it.
$exePath = Join-Path $verifyPath (Split-Path -Leaf $artifactPath)
Copy-Item -LiteralPath $artifactPath -Destination $exePath

# An isolated extraction folder proves the exe unpacks itself and keeps the runner's %TEMP% clean.
$previousSmokeImage = $env:TECH_BROWSER_SMOKE_IMAGE
$previousExtractBase = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
try {
    $env:TECH_BROWSER_SMOKE_IMAGE = $smokeImagePath
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $extractPath
    $process = Start-Process -FilePath $exePath -ArgumentList '--smoke-test' -WorkingDirectory $verifyPath -Wait -PassThru

    # The address-bar rules have no separate test project; the executable checks them itself.
    $selfTest = Start-Process -FilePath $exePath -ArgumentList '--self-test' -WorkingDirectory $verifyPath -Wait -PassThru
}
finally {
    $env:TECH_BROWSER_SMOKE_IMAGE = $previousSmokeImage
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $previousExtractBase
}

$applicationLogPath = Join-Path $verifyPath 'Tech-Browser.log'
if ($process.ExitCode -ne 0) {
    if (Test-Path -LiteralPath $applicationLogPath) { Get-Content -LiteralPath $applicationLogPath | Write-Host }
    throw "Packaged browser smoke test failed with exit code $($process.ExitCode)."
}
if (-not (Test-Path -LiteralPath $smokeImagePath)) {
    throw 'The packaged browser exited successfully but did not render the smoke-test image.'
}
if ($selfTest.ExitCode -ne 0) {
    Get-Content -LiteralPath $applicationLogPath | Write-Host
    throw "Address self-test failed with exit code $($selfTest.ExitCode)."
}
if (-not (Test-Path -LiteralPath $applicationLogPath)) {
    throw "Expected diagnostic log was not created beside the exe: $applicationLogPath"
}

$runtimeFiles = @('msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'libcef.dll')
foreach ($runtimeFile in $runtimeFiles) {
    if (-not (Get-ChildItem -LiteralPath $extractPath -Recurse -File -Filter $runtimeFile)) {
        throw "The exe did not unpack required file: $runtimeFile"
    }
}

$hash = Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256
$result = [pscustomobject]@{
    SmokeExit = $process.ExitCode
    SelfTestExit = $selfTest.ExitCode
    RenderedImage = $smokeImagePath
    UnpackedFiles = $runtimeFiles -join ', '
    SizeMiB = [math]::Round((Get-Item -LiteralPath $artifactPath).Length / 1MB, 1)
    SHA256 = $hash.Hash
}

Remove-Item -LiteralPath $verifyPath -Recurse -Force
$result | Format-List
