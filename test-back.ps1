$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$local = Join-Path $root '.local'
$dotnetHome = Join-Path $local 'dotnet-home'
$dotnetTools = Join-Path $dotnetHome '.dotnet\tools'
$appData = Join-Path $local 'appdata'
$nugetPackages = Join-Path $local 'nuget-packages'
$nugetHttpCache = Join-Path $local 'nuget-http-cache'
$src = Join-Path $root 'src'
$project = Join-Path $src 'LogViewer.BackSmoke\LogViewer.BackSmoke.csproj'

New-Item -ItemType Directory -Force -Path $dotnetHome, $dotnetTools, $appData, $nugetPackages, $nugetHttpCache | Out-Null

$env:DOTNET_CLI_HOME = $dotnetHome
$env:APPDATA = $appData
$env:NUGET_PACKAGES = $nugetPackages
$env:NUGET_HTTP_CACHE_PATH = $nugetHttpCache

& dotnet run --project $project -c Release --nologo -p:RestoreIgnoreFailedSources=true
if ($LASTEXITCODE -ne 0) {
    throw "LogViewer.BackSmoke failed with exit code $LASTEXITCODE"
}
