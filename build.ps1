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

& $dotnet build "$PSScriptRoot\BetterRichPresence\BetterRichPresence.csproj" -c $Configuration -nologo
exit $LASTEXITCODE
