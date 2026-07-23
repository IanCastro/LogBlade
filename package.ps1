param(
    [Parameter(Position = 0)]
    [string]$Path,

    [string]$Version = '0.1.0-beta.2',

    [switch]$RequireNativeAot
)

$ErrorActionPreference = 'Stop'

$semanticVersionPattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'
if ($Version -notmatch $semanticVersionPattern) {
    throw "Version must be a valid semantic version without a leading v: $Version"
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$local = Join-Path $root '.local'
$dotnetHome = Join-Path $local 'dotnet-home'
$dotnetTools = Join-Path $dotnetHome '.dotnet\tools'
$appData = Join-Path $local 'appdata'
$nugetPackages = Join-Path $local 'nuget-packages'
$nugetHttpCache = Join-Path $local 'nuget-http-cache'
$processTemp = Join-Path $local 'temp'
$src = Join-Path $root 'src'
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$aotPublish = Join-Path $artifacts 'publish-aot'
$project = Join-Path $src 'LogBlade.Front\LogBlade.Front.csproj'
$resolvedLogPath = $null

if (-not [string]::IsNullOrWhiteSpace($Path)) {
    $resolvedLogPath = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
}

New-Item -ItemType Directory -Force -Path $artifacts, $publish, $aotPublish, $dotnetHome, $dotnetTools, $appData, $nugetPackages, $nugetHttpCache, $processTemp | Out-Null

$env:DOTNET_CLI_HOME = $dotnetHome
$env:APPDATA = $appData
$env:NUGET_PACKAGES = $nugetPackages
$env:NUGET_HTTP_CACHE_PATH = $nugetHttpCache
$env:TEMP = $processTemp
$env:TMP = $processTemp

Remove-Item -Recurse -Force $aotPublish -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $publish -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $aotPublish, $publish | Out-Null

$nativeAotSucceeded = $false
& dotnet publish $project -c Release -r win-x64 -o $aotPublish --nologo -p:RestoreIgnoreFailedSources=true -p:PublishAot=true -p:SelfContained=true -p:Version=$Version -p:InformationalVersion=$Version -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -eq 0) {
    $nativeAotSucceeded = $true
    Remove-Item -Recurse -Force $publish -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $publish | Out-Null
    Move-Item -Force -Path (Join-Path $aotPublish '*') -Destination $publish
    Remove-Item -Recurse -Force $aotPublish -ErrorAction SilentlyContinue
}

if (-not $nativeAotSucceeded) {
    if ($RequireNativeAot) {
        throw 'NativeAOT publish failed and -RequireNativeAot was specified.'
    }

    Write-Warning 'NativeAOT publish failed or was unavailable; falling back to a self-contained direct-Win32 publish.'
    Remove-Item -Recurse -Force $publish -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $publish | Out-Null
    & dotnet publish $project -c Release -r win-x64 -o $publish --nologo -p:RestoreIgnoreFailedSources=true -p:PublishAot=false -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:UseAppHost=true -p:Version=$Version -p:InformationalVersion=$Version -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish fallback failed with exit code $LASTEXITCODE"
    }
}

$publishExe = Join-Path $publish 'LogBlade.exe'
if (Test-Path $publishExe) {
    $unexpectedFiles = @(Get-ChildItem -LiteralPath $publish -File | Where-Object { $_.Name -ne 'LogBlade.exe' -and $_.Extension -ne '.pdb' })
    if ($unexpectedFiles.Count -gt 0) {
        throw 'Publish did not produce a single-file application. Unexpected files: ' + (($unexpectedFiles | ForEach-Object Name) -join ', ')
    }

    Get-ChildItem -LiteralPath $publish -File -Filter '*.pdb' | Remove-Item -Force
    Write-Host "Packaged to $publish"
    if ($resolvedLogPath) {
        Start-Process -FilePath $publishExe -WorkingDirectory $root -ArgumentList @($resolvedLogPath) | Out-Null
    }
} else {
    throw "No runnable distribution artifact was produced in $publish"
}
