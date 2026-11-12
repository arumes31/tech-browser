[CmdletBinding()]
param(
    # Print the next unused stable patch on the VERSION release line instead of a development version.
    [switch]$NextRelease
)

# Same rules as the noxa repo: VERSION names the release line, git tags carry the released patches,
# and nothing is ever committed back.
$projectRoot = Split-Path -Parent $PSScriptRoot
$base = (Get-Content -LiteralPath (Join-Path $projectRoot 'VERSION') -Raw).Trim()
if ($base -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
    throw "VERSION must contain MAJOR.MINOR.PATCH, got '$base'."
}
$major = [int]$Matches[1]
$minor = [int]$Matches[2]
$patch = [int]$Matches[3]

# Source archives without git history can only produce a development version.
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot '.git'))) {
    if ($NextRelease) { throw 'A release version requires a git checkout with tags.' }
    return "$base-dev"
}

# A commit that already carries a release tag keeps that version, so reruns do not bump.
$exact = @(git -C $projectRoot tag --points-at HEAD) -match '^v\d+\.\d+\.\d+$' | Select-Object -First 1
if ($exact) { return $exact.Substring(1) }

if (-not $NextRelease) {
    return "$base-dev+$(git -C $projectRoot rev-parse --short HEAD)"
}

foreach ($tag in @(git -C $projectRoot tag --list 'v*')) {
    if ($tag -notmatch '^v(\d+)\.(\d+)\.(\d+)$') { continue }
    $tagMajor = [int]$Matches[1]
    $tagMinor = [int]$Matches[2]
    $tagPatch = [int]$Matches[3]
    if ($tagMajor -gt $major -or ($tagMajor -eq $major -and $tagMinor -gt $minor)) {
        throw "Stable tag '$tag' is newer than the VERSION release line '$base'. Raise VERSION first."
    }
    if ($tagMajor -eq $major -and $tagMinor -eq $minor -and $tagPatch -ge $patch) {
        $patch = $tagPatch + 1
    }
}
"$major.$minor.$patch"
