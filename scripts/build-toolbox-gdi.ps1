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
$publishPath = Join-Path $distPath 'gdi-publish'
$exePath = Join-Path $distPath "Tech-Browser-$Version-x64-gdi.exe"

$distFullPath = [System.IO.Path]::GetFullPath($distPath).TrimEnd('\')
$publishFullPath = [System.IO.Path]::GetFullPath($publishPath)
if (-not $publishFullPath.StartsWith("$distFullPath\", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean path outside dist: $publishFullPath"
}
foreach ($stale in @($publishPath, $exePath)) {
    if (Test-Path -LiteralPath $stale) {
        Remove-Item -LiteralPath $stale -Recurse -Force
    }
}

# The project file sets single-file publishing, trims unused files and adds the Visual C++ runtime.
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnetPath publish $projectPath -c Release -r win-x64 "-p:Version=$Version" -o $publishPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# Everything must be inside the exe; a loose file next to it would be missing on the endpoint.
$published = @(Get-ChildItem -LiteralPath $publishPath -Recurse -File)
if ($published.Count -ne 1 -or $published[0].Name -ne 'Tech Browser.exe') {
    throw "Expected only 'Tech Browser.exe' in the publish output, found: $($published.Name -join ', ')"
}

Move-Item -LiteralPath $published[0].FullName -Destination $exePath
Remove-Item -LiteralPath $publishPath -Recurse -Force

$artifact = Get-Item -LiteralPath $exePath
Write-Output "Created $($artifact.FullName) ($([math]::Round($artifact.Length / 1MB, 1)) MiB)"
