# Updates this game folder from the newest GitHub release, by itself:
# - a small update (DownhillRoguelite-update-<version>-for-<base>.zip) when one is made for this
#   folder's full release (base.txt);
# - otherwise the whole new Windows build, when the newest release is a new full release.
# Downloads are checked against GitHub's SHA-256 and unpacked to a temporary folder first, so a
# broken download never touches the game. Models\, logs and the player's saves are left alone.
#
# "Update game.bat" runs it by hand. The game runs its own copy (embedded in the game's code) when
# the player picks "Update" on the title screen: then it waits for the game to close, updates and
# starts the game again (-WaitPid, -Relaunch, and the chosen download in -Url/-Sha256/...).
# The rules for picking the update are the same as core/update/UpdatePlanner.cs: keep them in step.
#
# Exit codes: 0 done or already up to date, 10 an update is available (-CheckOnly), 1 failed.
param(
    [string]$GameDir = $PSScriptRoot,
    [int]$WaitPid = 0,
    [switch]$Relaunch,
    [switch]$IncludePrerelease,
    [switch]$CheckOnly,
    # A release list to read instead of GitHub's (a URL or a local .json file): tests.
    [string]$Feed = '',
    # The download the game already chose (skips asking GitHub again).
    [string]$Url = '',
    [string]$Sha256 = '',
    [ValidateSet('', 'Patch', 'Full')][string]$Kind = '',
    [string]$Version = '',
    [string]$Tag = '',
    [string]$Name = ''
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$repo = 'vstimac/DownhillRoguelite'
$exeName = 'DownhillRoguelite.exe'
$patchPrefix = 'DownhillRoguelite-update-'

function Say($text) { Write-Host "  $text" }

function Read-Line($name, $fallback) {
    $path = Join-Path $GameDir $name
    if (Test-Path -LiteralPath $path) {
        $line = Get-Content -LiteralPath $path -TotalCount 1
        if ($line) { return $line.Trim() }
    }
    return $fallback
}

# Ends the script: starts the game again when asked (also after a failure, so the player can play
# the version they have), and gives a hand-started run time to read the result.
function Finish([int]$code) {
    if ($Relaunch) {
        $exe = Join-Path $GameDir $exeName
        if (Test-Path -LiteralPath $exe) {
            if ($code -eq 0) { Say 'Starting the game...' } else { Say 'Starting the version you have in 10 seconds...'; Start-Sleep -Seconds 10 }
            try { Start-Process -FilePath $exe -WorkingDirectory $GameDir } catch { Say "Couldn't start the game: $($_.Exception.Message)" }
        }
        Start-Sleep -Seconds 2
    }
    exit $code
}

function Get-PatchVersion([string]$assetName, [string]$baseTag) {
    if (-not $baseTag -or -not $assetName.StartsWith($patchPrefix, [StringComparison]::OrdinalIgnoreCase)) { return $null }
    $suffix = "-for-$baseTag.zip"
    if (-not $assetName.EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase)) { return $null }
    $v = $assetName.Substring($patchPrefix.Length, $assetName.Length - $patchPrefix.Length - $suffix.Length)
    if ($v) { return $v }
    return $null
}

function Test-FullBuild([string]$assetName) {
    if ($assetName.StartsWith($patchPrefix, [StringComparison]::OrdinalIgnoreCase)) { return $false }
    return $assetName.EndsWith('-windows.zip', [StringComparison]::OrdinalIgnoreCase) -or $assetName.EndsWith('-windows-full.zip', [StringComparison]::OrdinalIgnoreCase)
}

function New-Offer($kind, $ver, $release, $asset) {
    $sha = ''
    if ($asset.digest -and ([string]$asset.digest).StartsWith('sha256:')) { $sha = ([string]$asset.digest).Substring(7).ToLowerInvariant() }
    $title = [string]$release.name
    if (-not $title) { $title = [string]$release.tag_name }
    return @{ Kind = $kind; Version = $ver; Tag = [string]$release.tag_name; Name = $title; Url = [string]$asset.browser_download_url; Size = [long]$asset.size; Sha256 = $sha; Asset = [string]$asset.name }
}

# Same rules as UpdatePlanner.Plan: the newest release with something for this folder decides.
function Find-Update($releases, [string]$baseTag, [string]$installed, [bool]$pre) {
    if (-not $installed) { $installed = $baseTag }
    $list = @()
    foreach ($r in $releases) {
        if ($r.draft) { continue }
        if ($r.prerelease -and -not $pre) { continue }
        $list += $r
    }
    $list = @($list | Sort-Object -Descending -Property @{ Expression = { if ($_.published_at) { [datetime]$_.published_at } elseif ($_.created_at) { [datetime]$_.created_at } else { [datetime]::MinValue } } })
    foreach ($r in $list) {
        $patch = $null; $patchVersion = $null; $full = $null
        foreach ($a in @($r.assets)) {
            if (-not $a) { continue }
            $n = [string]$a.name
            $v = Get-PatchVersion $n $baseTag
            if ($v -and -not $patch) { $patch = $a; $patchVersion = $v }
            elseif (Test-FullBuild $n) {
                if (-not $full -or (([string]$full.name).EndsWith('-windows-full.zip', [StringComparison]::OrdinalIgnoreCase) -and -not $n.EndsWith('-windows-full.zip', [StringComparison]::OrdinalIgnoreCase))) { $full = $a }
            }
        }
        if ($patch) {
            if ($patchVersion -ieq $installed) { return $null }
            return New-Offer 'Patch' $patchVersion $r $patch
        }
        if ($full) {
            if (([string]$r.tag_name -ieq $baseTag) -or ([string]$r.tag_name -ieq $installed)) { return $null }
            return New-Offer 'Full' ([string]$r.tag_name) $r $full
        }
    }
    return $null
}

function Get-Releases {
    if ($Feed) {
        if (Test-Path -LiteralPath $Feed) { return ConvertFrom-Json -InputObject (Get-Content -LiteralPath $Feed -Raw) }
        return Invoke-RestMethod -Uri $Feed -Headers @{ 'User-Agent' = 'DownhillRoguelite-updater' }
    }
    return Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases?per_page=20" -Headers @{ 'User-Agent' = 'DownhillRoguelite-updater'; 'Accept' = 'application/vnd.github+json' }
}

# Downloads with a progress line (a local path or file:// URL is copied: tests).
function Get-Download([string]$from, [string]$to, [long]$expected) {
    if ($from.StartsWith('file:///')) { $from = ([Uri]$from).LocalPath }
    if (Test-Path -LiteralPath $from) { Copy-Item -LiteralPath $from -Destination $to -Force; return }
    Add-Type -AssemblyName System.Net.Http
    $client = New-Object System.Net.Http.HttpClient
    $client.Timeout = [TimeSpan]::FromMinutes(60)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd('DownhillRoguelite-updater/2.0')
    try {
        $response = $client.GetAsync($from, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        [void]$response.EnsureSuccessStatusCode()
        $total = $expected
        if ($response.Content.Headers.ContentLength) { $total = [long]$response.Content.Headers.ContentLength }
        $source = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $file = [IO.File]::Create($to)
        try {
            $buffer = New-Object byte[] (1MB)
            $done = [long]0
            $clock = [Diagnostics.Stopwatch]::StartNew()
            $shown = -1
            while (($read = $source.Read($buffer, 0, $buffer.Length)) -gt 0) {
                $file.Write($buffer, 0, $read)
                $done += $read
                $percent = 0
                if ($total -gt 0) { $percent = [int]($done * 100 / $total) }
                if ($percent -ne $shown) {
                    $shown = $percent
                    $speed = $done / 1MB / [Math]::Max($clock.Elapsed.TotalSeconds, 0.1)
                    $bars = [int]($percent / 4)
                    Write-Host -NoNewline ("`r  [{0}{1}] {2,3}%  {3:0} / {4:0} MB  {5:0.0} MB/s   " -f ('#' * $bars), ('.' * (25 - $bars)), $percent, ($done / 1MB), ($total / 1MB), $speed)
                }
            }
            Write-Host ''
        } finally {
            $file.Dispose()
            $source.Dispose()
        }
    } finally {
        $client.Dispose()
    }
}

# Copies every file under $from over $to, creating folders as needed.
function Copy-Over([string]$from, [string]$to) {
    $fromFull = (Resolve-Path -LiteralPath $from).Path.TrimEnd('\') + '\'
    foreach ($file in Get-ChildItem -LiteralPath $from -Recurse -File -Force) {
        $relative = $file.FullName.Substring($fromFull.Length)
        $target = Join-Path $to $relative
        $folder = Split-Path -Parent $target
        if (-not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
}

try {
    Write-Host ''
    Write-Host '  Downhill Roguelite: update' -ForegroundColor DarkYellow
    Write-Host ''
    $GameDir = (Resolve-Path -LiteralPath $GameDir).Path

    if ($WaitPid -gt 0) {
        Say 'Waiting for the game to close...'
        $game = Get-Process -Id $WaitPid -ErrorAction SilentlyContinue
        if ($game) { $game.WaitForExit(60000) | Out-Null }
    }
    if (-not $CheckOnly) {
        $running = @(Get-Process -Name 'DownhillRoguelite' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and ($_.Path -ieq (Join-Path $GameDir $exeName)) })
        if ($running.Count -gt 0) {
            Say 'Close the game first, then run this again.'
            Finish 1
        }
    }

    $base = Read-Line 'base.txt' ''
    $installed = Read-Line 'version.txt' $base
    if (-not $installed) { $installed = '(unknown)' }

    if (-not $Url) {
        Say "Installed: $installed. Checking GitHub..."
        $offer = Find-Update (Get-Releases) $base $installed ([bool]$IncludePrerelease)
        if (-not $offer) {
            Say "You have the newest version ($installed)."
            Finish 0
        }
    } else {
        if (-not $Kind) { throw '-Url needs -Kind Patch or Full' }
        $offer = @{ Kind = $Kind; Version = $Version; Tag = $Tag; Name = $Name; Url = $Url; Size = [long]0; Sha256 = $Sha256.ToLowerInvariant(); Asset = '' }
        if (-not $offer.Version) { $offer.Version = $Tag }
        if (-not $offer.Name) { $offer.Name = $offer.Version }
    }
    if ($CheckOnly) {
        Say ("Update available: {0} ({1}, {2:0.0} MB)" -f $offer.Name, $offer.Kind, ($offer.Size / 1MB))
        exit 10
    }

    # The folder must be writable (not under Program Files without admin rights).
    $probe = Join-Path $GameDir '.update-write-test'
    try { Set-Content -LiteralPath $probe -Value 'ok'; Remove-Item -LiteralPath $probe -Force }
    catch { throw "can't write to $GameDir. Move the game folder somewhere you own (Documents, Desktop) and try again" }

    $work = Join-Path ([IO.Path]::GetTempPath()) ('downhill-update-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
        $zip = Join-Path $work 'download.zip'
        if ($offer.Kind -eq 'Full') { Say "New version: $($offer.Name). Downloading the whole game:" }
        else { Say "New version: $($offer.Name). Downloading the update:" }
        Get-Download $offer.Url $zip $offer.Size

        if ($offer.Sha256) {
            $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($actual -ne $offer.Sha256) { throw 'the download is damaged (its checksum does not match). Nothing was changed; try again' }
            Say 'Download checked.'
        }

        Say 'Unpacking...'
        $staging = Join-Path $work 'files'
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($zip, $staging)
        $root = $staging
        if (-not (Test-Path -LiteralPath (Join-Path $root $exeName))) {
            $inner = @(Get-ChildItem -LiteralPath $staging -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName $exeName) })
            if ($inner.Count -eq 1) { $root = $inner[0].FullName }
        }
        if ($offer.Kind -eq 'Full') {
            if (-not (Test-Path -LiteralPath (Join-Path $root $exeName)) -or -not (Test-Path -LiteralPath (Join-Path $root 'DownhillRoguelite.pck'))) {
                throw 'the download has no game in it. Nothing was changed'
            }
        } elseif (-not (Test-Path -LiteralPath (Join-Path $root 'patches\update.pck'))) {
            throw 'the update has no patch in it. Nothing was changed'
        }

        Say 'Installing...'
        if ($offer.Kind -eq 'Full') {
            # A new full release replaces every earlier small update.
            $patches = Join-Path $GameDir 'patches'
            if (Test-Path -LiteralPath $patches) { Remove-Item -LiteralPath $patches -Recurse -Force }
            foreach ($marker in 'version.txt', 'base.txt') {
                $path = Join-Path $GameDir $marker
                if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
            }
        }
        Copy-Over $root $GameDir
        if ($offer.Kind -eq 'Full' -and -not (Test-Path -LiteralPath (Join-Path $root 'base.txt'))) {
            Set-Content -LiteralPath (Join-Path $GameDir 'base.txt') -Value $offer.Tag
        }
        if ($offer.Version) { Set-Content -LiteralPath (Join-Path $GameDir 'version.txt') -Value $offer.Version }
    } finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
    Say "Updated to $($offer.Name). Have a good descent!"
    Finish 0
} catch {
    Write-Host ''
    Say "The update didn't work: $($_.Exception.Message)."
    Say "You can always download the game from https://github.com/$repo/releases"
    Finish 1
}
