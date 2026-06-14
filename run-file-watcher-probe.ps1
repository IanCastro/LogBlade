param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Path
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$local = Join-Path $root '.local'
$dotnetHome = Join-Path $local 'dotnet-home'
$dotnetTools = Join-Path $dotnetHome '.dotnet\tools'
$appData = Join-Path $local 'appdata'
$nugetPackages = Join-Path $local 'nuget-packages'
$nugetHttpCache = Join-Path $local 'nuget-http-cache'
$project = Join-Path $root 'src\FileWatcherProbe\FileWatcherProbe.csproj'
try {
    $resolvedPath = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
}
catch {
    if ([System.IO.Path]::IsPathRooted($Path)) {
        $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    }
    else {
        $resolvedPath = [System.IO.Path]::GetFullPath((Join-Path $PWD.ProviderPath $Path))
    }
}

New-Item -ItemType Directory -Force -Path $dotnetHome, $dotnetTools, $appData, $nugetPackages, $nugetHttpCache | Out-Null

$env:DOTNET_CLI_HOME = $dotnetHome
$env:APPDATA = $appData
$env:NUGET_PACKAGES = $nugetPackages
$env:NUGET_HTTP_CACHE_PATH = $nugetHttpCache

& dotnet run --project $project -c Release -- $resolvedPath
if ($LASTEXITCODE -ne 0) {
    throw "FileWatcherProbe failed with exit code $LASTEXITCODE"
}
