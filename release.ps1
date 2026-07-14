param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [switch]$RequireNativeAot
)

$ErrorActionPreference = 'Stop'

$semanticVersionPattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'
if ($Version -notmatch $semanticVersionPattern) {
    throw "Version must be a valid semantic version without a leading v: $Version"
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$release = Join-Path $artifacts 'release'
$stage = Join-Path $release 'stage'
$packageName = "LogBlade-$Version-win-x64"
$zipPath = Join-Path $release "$packageName.zip"
$checksumsPath = Join-Path $release 'SHA256SUMS.txt'

& (Join-Path $root 'package.ps1') -Version $Version -RequireNativeAot:$RequireNativeAot
if ($LASTEXITCODE -ne 0) {
    throw "package.ps1 failed with exit code $LASTEXITCODE"
}

Remove-Item -Recurse -Force $release -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item -LiteralPath (Join-Path $publish 'LogBlade.exe') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $stage

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item -Recurse -Force $stage

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
$checksumLine = "$hash *$([System.IO.Path]::GetFileName($zipPath))`n"
[System.IO.File]::WriteAllText($checksumsPath, $checksumLine, [System.Text.UTF8Encoding]::new($false))

Write-Host "Release package: $zipPath"
Write-Host "Checksums: $checksumsPath"
