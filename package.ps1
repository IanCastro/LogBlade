param(
    [Parameter(Position = 0)]
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
$src = Join-Path $root 'src'
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$aotPublish = Join-Path $artifacts 'publish-aot'
$project = Join-Path $src 'LogBlade.Front\LogBlade.Front.csproj'
$resolvedLogPath = $null

if (-not [string]::IsNullOrWhiteSpace($Path)) {
    $resolvedLogPath = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
}

New-Item -ItemType Directory -Force -Path $artifacts, $publish, $aotPublish, $dotnetHome, $dotnetTools, $appData, $nugetPackages, $nugetHttpCache | Out-Null

$env:DOTNET_CLI_HOME = $dotnetHome
$env:APPDATA = $appData
$env:NUGET_PACKAGES = $nugetPackages
$env:NUGET_HTTP_CACHE_PATH = $nugetHttpCache

Remove-Item -Recurse -Force $aotPublish -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $publish -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $aotPublish, $publish | Out-Null

$nativeAotSucceeded = $false
& dotnet publish $project -c Release -r win-x64 -o $aotPublish --nologo -p:RestoreIgnoreFailedSources=true -p:PublishAot=true -p:SelfContained=true
if ($LASTEXITCODE -eq 0) {
    $nativeAotSucceeded = $true
    Remove-Item -Recurse -Force $publish -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $publish | Out-Null
    Move-Item -Force -Path (Join-Path $aotPublish '*') -Destination $publish
    Remove-Item -Recurse -Force $aotPublish -ErrorAction SilentlyContinue
}

if (-not $nativeAotSucceeded) {
    Write-Warning 'NativeAOT publish failed or was unavailable; falling back to a self-contained direct-Win32 publish.'
    Remove-Item -Recurse -Force $publish -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $publish | Out-Null
    & dotnet publish $project -c Release -o $publish --nologo -p:RestoreIgnoreFailedSources=true -p:PublishAot=false -p:UseAppHost=true
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish fallback failed with exit code $LASTEXITCODE"
    }
}

$publishExe = Join-Path $publish 'LogBlade.exe'
if (Test-Path $publishExe) {
    Write-Host "Packaged to $publish"
    if ($resolvedLogPath) {
        Start-Process -FilePath $publishExe -WorkingDirectory $root -ArgumentList @($resolvedLogPath) | Out-Null
    }
} else {
    throw "No runnable distribution artifact was produced in $publish"
}
