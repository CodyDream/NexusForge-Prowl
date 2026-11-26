# Builds Prowl with the project-local .NET 11 SDK (.dotnet) and the local Anthology sources (../Anthology).
# Usage:  powershell -ExecutionPolicy Bypass -File build.ps1 [-Configuration Release] [-Project Prowl.Editor/Prowl.Editor.csproj]

param(
    [string]$Configuration = "Debug",
    [string]$Project = "Prowl.slnx"
)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnetRoot = Join-Path $root ".dotnet"
$dotnet = Join-Path $dotnetRoot "dotnet.exe"

if (-not (Test-Path $dotnet)) {
    throw ".NET 11 SDK not found at $dotnet. Reinstall it with:`n  https://dot.net/v1/dotnet-install.ps1 -Version 11.0.100 -InstallDir `"$dotnetRoot`""
}

if (-not (Test-Path (Join-Path $root "prowl.local.props"))) {
    throw "prowl.local.props is missing, so the build would use the (older) NuGet Anthology packages."
}

# The local SDK has to be on PATH too: some projects (Quill's shader generator) shell out to `dotnet` themselves.
$env:DOTNET_ROOT = $dotnetRoot
if ($env:PATH -notlike "$dotnetRoot*") { $env:PATH = "$dotnetRoot;$env:PATH" }

& $dotnet build (Join-Path $root $Project) -c $Configuration
exit $LASTEXITCODE
