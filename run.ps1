param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Path
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root 'artifacts\publish\LogViewer.exe'

if (-not (Test-Path $exe)) {
    throw "Build output not found: $exe. Run build.ps1 first."
}

& $exe $Path
