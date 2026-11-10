# Updates this game folder from the newest GitHub release: downloads only the small update
# (DownhillRoguelite-update-<version>-for-<base>.zip) made for this folder's full release and
# unpacks it over the folder. If the newest release needs a new full download, it says so.
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$repo = 'vstimac/DownhillRoguelite'
$folder = $PSScriptRoot

function Read-Line($name, $fallback) {
    $path = Join-Path $folder $name
    if (Test-Path $path) { return (Get-Content $path -TotalCount 1).Trim() }
    return $fallback
}

$base = Read-Line 'base.txt' ''
$installed = Read-Line 'version.txt' $base
if (-not $base) {
    Write-Host 'This folder has no base.txt, so it cannot take small updates.'
    Write-Host "Download the full game once from https://github.com/$repo/releases/latest"
    exit 1
}
if (Get-Process -Name 'DownhillRoguelite' -ErrorAction SilentlyContinue) {
    Write-Host 'Close the game first, then run this again.'
    exit 1
}

Write-Host "Installed: $installed (full release $base). Checking GitHub..."
$releases = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases?per_page=20" -Headers @{ 'User-Agent' = 'DownhillRoguelite-updater' }
$suffix = "-for-$base.zip"
$found = $null
$newestFull = $null
foreach ($release in $releases) {
    if ($release.draft) { continue }
    foreach ($asset in $release.assets) {
        if (-not $found -and $asset.name -like 'DownhillRoguelite-update-*' -and $asset.name.EndsWith($suffix)) { $found = $asset }
        if (-not $newestFull -and $asset.name -like '*-windows.zip' -and $asset.name -notlike '*-update-*') { $newestFull = $release }
    }
    if ($found) { break }
}
if (-not $found) {
    if ($newestFull -and $newestFull.tag_name -ne $base) {
        Write-Host "A new full version is out ($($newestFull.name)). Download it once from:"
        Write-Host $newestFull.html_url
    } else {
        Write-Host 'You have the newest version.'
    }
    exit 0
}
$version = $found.name.Substring('DownhillRoguelite-update-'.Length)
$version = $version.Substring(0, $version.Length - $suffix.Length)
if ($version -eq $installed) {
    Write-Host "You have the newest version ($installed)."
    exit 0
}

$zip = Join-Path $env:TEMP $found.name
Write-Host "Downloading $version ($([math]::Round($found.size / 1MB, 1)) MB)..."
Invoke-WebRequest -UseBasicParsing -Uri $found.browser_download_url -OutFile $zip
Expand-Archive -Path $zip -DestinationPath $folder -Force
Remove-Item $zip -ErrorAction SilentlyContinue
Write-Host "Updated to $version. Have a good descent!"
