param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Path
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildExe = Join-Path $root 'artifacts\build\LogViewer-CSharp.exe'
$publishExe = Join-Path $root 'artifacts\publish\LogViewer-CSharp.exe'
$resolvedPath = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path

$exe = $null
if (Test-Path $buildExe) {
    $exe = $buildExe
}
elseif (Test-Path $publishExe) {
    $exe = $publishExe
}

if (-not $exe) {
    throw "No runnable artifact was found. Run build.ps1 for the normal build or package.ps1 for the distribution publish."
}

Start-Process -FilePath $exe -WorkingDirectory $root -ArgumentList @($resolvedPath) | Out-Null
