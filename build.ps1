param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

# Prefer a dotnet that has an SDK: the one on PATH may only ship the runtime.
$candidates = @(
    (Get-Command dotnet -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe",
    "$env:ProgramFiles\dotnet\dotnet.exe"
) | Where-Object { $_ -and (Test-Path $_) }

$dotnet = $candidates | Where-Object { & $_ --list-sdks 2>$null } | Select-Object -First 1
if (-not $dotnet) {
    Write-Error "No .NET SDK found. Install one from https://aka.ms/dotnet/download"
    exit 1
}

# Playnite loads the Debug build as an external extension and locks its DLL while running.
# It saves its configuration on exit, so wait for the user to quit it instead of killing it.
$playnite = $null
if ($Configuration -eq 'Debug') {
    $playnite = Get-Process Playnite.DesktopApp, Playnite.FullscreenApp -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($playnite) {
        $playnitePath = $playnite.Path
        Write-Host "Playnite is running and locks the plugin DLL. Quit it from its tray icon (right-click > Exit)..." -ForegroundColor Yellow
        $playnite.WaitForExit()
    }
}

& $dotnet build "$PSScriptRoot\BetterRichPresence\BetterRichPresence.csproj" -c $Configuration -nologo
$buildExitCode = $LASTEXITCODE

if ($playnite -and $buildExitCode -eq 0) {
    Write-Host "Restarting Playnite..." -ForegroundColor Green
    Start-Process $playnitePath
}

exit $buildExitCode
