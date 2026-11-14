param(
    [ValidateSet('Game', 'Ragdoll', 'Coop')]
    [string]$Target = 'Game',
    [string]$ProjectDirectory = '',
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
$gameDirectory = Split-Path -Parent $PSScriptRoot
if (-not [string]::IsNullOrWhiteSpace($ProjectDirectory)) { $gameDirectory = $ProjectDirectory }
$repositoryDirectory = Split-Path -Parent $gameDirectory
$workspaceDirectory = Split-Path -Parent $repositoryDirectory
$projectFile = Join-Path $gameDirectory 'DownhillRoguelite.csproj'
$logDirectory = Join-Path $repositoryDirectory '.validation\desktop-launcher'

try {
    if (-not (Test-Path -LiteralPath $projectFile)) {
        throw "Projekt nije pronadjen: $projectFile"
    }
    $dotnetCommand = Get-Command dotnet -ErrorAction Stop
    [xml]$project = Get-Content -LiteralPath $projectFile -Raw
    if ([string]$project.Project.Sdk -notmatch '^Godot\.NET\.Sdk/([0-9.]+)$') {
        throw 'Nije moguce procitati Godot .NET verziju iz projekta.'
    }
    $requiredVersion = $Matches[1]
    $engineDirectory = Join-Path $workspaceDirectory "Godot_v${requiredVersion}-stable_mono_win64"
    $engineConsole = Join-Path $engineDirectory "Godot_v${requiredVersion}-stable_mono_win64_console.exe"
    $engineGame = Join-Path $engineDirectory "Godot_v${requiredVersion}-stable_mono_win64.exe"
    if (-not (Test-Path -LiteralPath $engineConsole) -or -not (Test-Path -LiteralPath $engineGame)) {
        throw "Nedostaje Godot .NET $requiredVersion u $engineDirectory"
    }
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    Write-Host "Projekt: $gameDirectory"
    Write-Host 'Gradim aktualni lokalni kod...' -ForegroundColor Cyan
    & $dotnetCommand.Source build $projectFile --configuration Debug --nologo
    if ($LASTEXITCODE -ne 0) {
        throw 'Build nije uspio. Detalji su ispisani iznad.'
    }

    Write-Host 'Provjeravam i uvozim aktualne Godot resurse...' -ForegroundColor Cyan
    $importOutput = Join-Path $logDirectory 'import.log'
    $importErrors = Join-Path $logDirectory 'import-errors.log'
    $importArguments = @('--headless', '--editor', '--path', ('"{0}"' -f $gameDirectory), '--import')
    $importProcess = Start-Process -FilePath $engineConsole -ArgumentList $importArguments -WorkingDirectory $gameDirectory -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $importOutput -RedirectStandardError $importErrors
    $importText = (Get-Content -LiteralPath $importOutput -Raw) + (Get-Content -LiteralPath $importErrors -Raw)
    $problemLines = @($importText -split '\r?\n' | Where-Object {
        $_ -match 'ERROR|Unhandled exception|Exception:' -and
        $_ -notmatch 'addons/[^ ]*/~|Error copying library|Can.t open GDExtension dynamic library|Error loading extension'
    })
    if ($problemLines.Count -gt 0 -or $importProcess.ExitCode -ne 0) {
        $problemLines | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        throw "Godot import nije uspio. Logovi: $importOutput i $importErrors"
    }

    $gameArguments = @('--path', ('"{0}"' -f $gameDirectory), '--rendering-method', 'gl_compatibility', '--rendering-driver', 'opengl3', '--log-file', ('"{0}"' -f (Join-Path $logDirectory "$Target.log")))
    if ($Target -eq 'Ragdoll') {
        $gameArguments += 'res://debug/TestActiveRagdoll.tscn'
    }
    if ($Target -eq 'Coop') {
        $gameArguments += @('--', '--menu-page=coop', '--no-update-check')
    }
    if ($CheckOnly) {
        Write-Host "Provjera OK: $Target; Godot $requiredVersion; build i resursi su aktualni." -ForegroundColor Green
        exit 0
    }
    if ($Target -eq 'Ragdoll') {
        $trialSettings = Join-Path $repositoryDirectory '.validation\manual-ragdoll-user'
        New-Item -ItemType Directory -Force -Path $trialSettings | Out-Null
        $env:APPDATA = $trialSettings
    }
    $gameProcess = Start-Process -FilePath $engineGame -ArgumentList $gameArguments -WorkingDirectory $gameDirectory -WindowStyle Normal -PassThru
    Write-Host "Pokrenuto: $Target (PID $($gameProcess.Id))." -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "GRESKA: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
