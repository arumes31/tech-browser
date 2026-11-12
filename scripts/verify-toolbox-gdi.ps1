[CmdletBinding()]
param(
    [string]$Artifact
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $Artifact) {
    $Artifact = "dist\Tech-Browser-$(& (Join-Path $PSScriptRoot 'version.ps1'))-x64-gdi.scapp"
}
$distPath = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'dist')).TrimEnd('\')
$artifactPath = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $Artifact))
$verifyPath = [System.IO.Path]::GetFullPath((Join-Path $distPath 'verify-gdi-scapp'))
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

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($artifactPath)
try {
    $entryCount = $zip.Entries.Count
    $firstName = $zip.Entries | Sort-Object FullName | Select-Object -First 1 -ExpandProperty FullName
}
finally {
    $zip.Dispose()
}

if ($firstName -ne '0-Tech-Browser-GDI.vbs') {
    throw "Unexpected first toolbox entry: $firstName"
}

[System.IO.Compression.ZipFile]::ExtractToDirectory($artifactPath, $verifyPath)
$launcherPath = Join-Path $verifyPath '0-Tech-Browser-GDI.vbs'
foreach ($required in @($launcherPath, (Join-Path $verifyPath '7za.exe'), (Join-Path $verifyPath 'payload.7z'))) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required package file is missing: $required"
    }
}

$previousSmokeImage = $env:TECH_BROWSER_SMOKE_IMAGE
try {
    $env:TECH_BROWSER_SMOKE_IMAGE = $smokeImagePath
    $wscriptPath = Join-Path $env:SystemRoot 'System32\wscript.exe'
    $process = Start-Process -FilePath $wscriptPath `
        -ArgumentList @('//B', '//NoLogo', ('"{0}"' -f $launcherPath), '--smoke-test') `
        -WorkingDirectory $verifyPath -Wait -PassThru
}
finally {
    $env:TECH_BROWSER_SMOKE_IMAGE = $previousSmokeImage
}

$applicationLogPath = Join-Path $verifyPath 'Tech-Browser.log'
if ($process.ExitCode -ne 0) {
    if (Test-Path -LiteralPath $applicationLogPath) { Get-Content -LiteralPath $applicationLogPath | Write-Host }
    throw "Packaged browser smoke test failed with exit code $($process.ExitCode)."
}
if (-not (Test-Path -LiteralPath $smokeImagePath)) {
    throw 'The packaged browser exited successfully but did not render the smoke-test image.'
}

$runtimeFiles = @('msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll')
foreach ($runtimeFile in $runtimeFiles) {
    $runtimePath = Join-Path $verifyPath "app\$runtimeFile"
    if (-not (Test-Path -LiteralPath $runtimePath)) {
        throw "The packaged browser is missing Visual C++ runtime file: $runtimeFile"
    }
}

$launcherLogPath = Join-Path $verifyPath 'Tech-Browser-launcher.log'
foreach ($logPath in @($launcherLogPath, $applicationLogPath)) {
    if (-not (Test-Path -LiteralPath $logPath)) {
        throw "Expected diagnostic log was not created: $logPath"
    }
}

# The address-bar rules have no separate test project; the executable checks them itself.
$selfTest = Start-Process -FilePath (Join-Path $verifyPath 'app\Tech Browser.exe') -ArgumentList '--self-test' `
    -WorkingDirectory $verifyPath -Wait -PassThru
if ($selfTest.ExitCode -ne 0) {
    Get-Content -LiteralPath $applicationLogPath | Write-Host
    throw "Address self-test failed with exit code $($selfTest.ExitCode)."
}

$hash = Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256
$result = [pscustomobject]@{
    SmokeExit = $process.ExitCode
    RenderedImage = $smokeImagePath
    RuntimeFiles = $runtimeFiles -join ', '
    DiagnosticLogs = 'Tech-Browser-launcher.log, Tech-Browser.log'
    Entries = $entryCount
    FirstAlphabetical = $firstName
    SizeMiB = [math]::Round((Get-Item -LiteralPath $artifactPath).Length / 1MB, 1)
    SHA256 = $hash.Hash
}

Remove-Item -LiteralPath $verifyPath -Recurse -Force
$result | Format-List
