[CmdletBinding()]
param(
    # Release builds pass the tag version; local builds use package.json.
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $Version) {
    $Version = (Get-Content -LiteralPath (Join-Path $projectRoot 'package.json') -Raw | ConvertFrom-Json).version
}
$localDotnetPath = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnetPath = if (Test-Path -LiteralPath $localDotnetPath) {
    $localDotnetPath
}
else {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw 'The .NET 8 SDK is required. Install dotnet or place it in .tools\dotnet.'
    }
    $dotnetCommand.Source
}
$projectPath = Join-Path $projectRoot 'native\TechBrowser.Gdi\TechBrowser.Gdi.csproj'
$distPath = Join-Path $projectRoot 'dist'
$publishPath = Join-Path $distPath 'gdi-unpacked'
$stagingPath = Join-Path $distPath 'scapp-gdi-staging'
$payloadPath = Join-Path $stagingPath 'payload.7z'
$zipPath = Join-Path $distPath "Tech-Browser-$version-x64-gdi.zip"
$scappPath = Join-Path $distPath "Tech-Browser-$version-x64-gdi.scapp"
$launcherPath = Join-Path $projectRoot 'toolbox\0-Tech-Browser-GDI.vbs'
$sevenZipPath = Join-Path $projectRoot 'node_modules\7zip-bin\win\x64\7za.exe'
$sevenZipLicensePath = Join-Path $projectRoot 'node_modules\7zip-bin\LICENSE.txt'

$distFullPath = [System.IO.Path]::GetFullPath($distPath).TrimEnd('\')
foreach ($candidate in @($publishPath, $stagingPath)) {
    $candidateFullPath = [System.IO.Path]::GetFullPath($candidate)
    if (-not $candidateFullPath.StartsWith("$distFullPath\", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean path outside dist: $candidateFullPath"
    }
}

foreach ($directory in @($publishPath, $stagingPath)) {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnetPath publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false "-p:Version=$Version" -o $publishPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path -LiteralPath (Join-Path $publishPath 'Tech Browser.exe'))) {
    throw 'The GDI browser executable was not published.'
}

# CefSharp 138+ is built with Visual C++ 2022. Deploy the required x64 runtime
# beside the executable so endpoints do not need the redistributable installed.
$vcRuntimeFiles = @('msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll')
foreach ($runtimeFile in $vcRuntimeFiles) {
    $runtimeSource = Join-Path $env:WINDIR "System32\$runtimeFile"
    if (-not (Test-Path -LiteralPath $runtimeSource)) {
        throw "Required Visual C++ runtime file not found: $runtimeSource"
    }
    Copy-Item -LiteralPath $runtimeSource -Destination $publishPath -Force
}

New-Item -ItemType Directory -Path $stagingPath | Out-Null
Copy-Item -LiteralPath $launcherPath -Destination $stagingPath -Force
Copy-Item -LiteralPath $sevenZipPath -Destination (Join-Path $stagingPath '7za.exe') -Force
Copy-Item -LiteralPath $sevenZipLicensePath -Destination (Join-Path $stagingPath '7zip-license.txt') -Force

Push-Location $publishPath
try {
    & $sevenZipPath a -t7z $payloadPath '.\*' -mx=5 -m0=lzma2 -md=32m -mmt=on -ms=on
    if ($LASTEXITCODE -ne 0) {
        throw "7-Zip failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

foreach ($artifact in @($zipPath, $scappPath)) {
    if (Test-Path -LiteralPath $artifact) {
        Remove-Item -LiteralPath $artifact -Force
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stagingPath,
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false
)
Move-Item -LiteralPath $zipPath -Destination $scappPath
Remove-Item -LiteralPath $stagingPath -Recurse -Force

$artifact = Get-Item -LiteralPath $scappPath
Write-Output "Created $($artifact.FullName) ($([math]::Round($artifact.Length / 1MB, 1)) MiB)"
