$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$local = Join-Path $root '.local'
$dotnetHome = Join-Path $local 'dotnet-home'
$dotnetTools = Join-Path $dotnetHome '.dotnet\tools'
$appData = Join-Path $local 'appdata'
$nugetPackages = Join-Path $local 'nuget-packages'
$nugetHttpCache = Join-Path $local 'nuget-http-cache'
$src = Join-Path $root 'src'
$artifacts = Join-Path $root 'artifacts'
$build = Join-Path $artifacts 'build'
$project = Join-Path $src 'LogViewer.csproj'

New-Item -ItemType Directory -Force -Path $artifacts, $build, $dotnetHome, $dotnetTools, $appData, $nugetPackages, $nugetHttpCache | Out-Null

$env:DOTNET_CLI_HOME = $dotnetHome
$env:APPDATA = $appData
$env:NUGET_PACKAGES = $nugetPackages
$env:NUGET_HTTP_CACHE_PATH = $nugetHttpCache

Remove-Item -Recurse -Force $build -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $build | Out-Null

& dotnet build $project -c Release -o $build --nologo -p:RestoreIgnoreFailedSources=true -p:UseAppHost=true
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

if (Test-Path (Join-Path $build 'LogViewer-CSharp.exe')) {
    Write-Host "Built to $build"
} else {
    throw "No runnable artifact was produced in $build"
}
