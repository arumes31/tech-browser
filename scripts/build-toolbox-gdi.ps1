[CmdletBinding()]
param(
    # Release builds pass the tag version; other builds get a development version.
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $Version) {
    $Version = & (Join-Path $PSScriptRoot 'version.ps1')
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

# Shrink the payload: transfer and first-launch extraction dominate startup in Backstage.
# Chromium UI strings: keep English and German. Web page content is unaffected.
Get-ChildItem -LiteralPath (Join-Path $publishPath 'locales') -File |
    Where-Object { $_.Name -notmatch '^(en-US|de)(_|\.)' } |
    Remove-Item -Force
# The browser runs with the GPU disabled, so WebGPU and SwiftShader (software WebGL) are never loaded.
# libEGL, libGLESv2 and d3dcompiler_47 must stay: CefSharp refuses to start without them.
# The .NET self-contained runtime also ships WPF, debugger support and satellite resources
# that a WinForms-only app does not load.
$unusedFiles = @(
    'dxcompiler.dll', 'dxil.dll', 'vk_swiftshader.dll', 'vk_swiftshader_icd.json', 'vulkan-1.dll',
    'Presentation*.dll', 'wpfgfx_cor3.dll', 'D3DCompiler_47_cor3.dll', 'PenImc_cor3.dll', 'ReachFramework.dll',
    'System.Xaml.dll', 'System.Windows.Controls.Ribbon.dll', 'System.Windows.Presentation.dll',
    'System.Windows.Input.Manipulations.dll', 'UIAutomation*.dll', 'WindowsFormsIntegration.dll',
    'System.Printing.dll', 'DirectWriteForwarder.dll', 'vcruntime140_cor3.dll',
    'createdump.exe', 'mscordaccore*.dll', 'mscordbi.dll', 'Microsoft.DiaSymReader.Native.amd64.dll', '*.pdb'
)
foreach ($pattern in $unusedFiles) {
    Get-ChildItem -LiteralPath $publishPath -File -Filter $pattern | Remove-Item -Force
}
Get-ChildItem -LiteralPath $publishPath -Directory |
    Where-Object { $_.Name -ne 'locales' -and -not (Get-ChildItem -LiteralPath $_.FullName -File | Where-Object { $_.Name -notlike '*.resources.dll' }) } |
    Remove-Item -Recurse -Force

New-Item -ItemType Directory -Path $stagingPath | Out-Null
Copy-Item -LiteralPath $launcherPath -Destination $stagingPath -Force
Copy-Item -LiteralPath $sevenZipPath -Destination (Join-Path $stagingPath '7za.exe') -Force
Copy-Item -LiteralPath $sevenZipLicensePath -Destination (Join-Path $stagingPath '7zip-license.txt') -Force

Push-Location $publishPath
try {
    # A 256 MB dictionary covers libcef.dll; two threads keep compression memory within a CI runner.
    & $sevenZipPath a -t7z $payloadPath '.\*' -mx=9 -m0=lzma2 -md=256m -mfb=273 -mmt=2 -ms=on
    if ($LASTEXITCODE -ne 0) {
        throw "7-Zip failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

# The launcher compares this identity with the extracted copy to decide whether to extract again.
"$Version $((Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash)" |
    Set-Content -LiteralPath (Join-Path $stagingPath 'payload.id') -Encoding Ascii

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
