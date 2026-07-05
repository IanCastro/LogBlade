$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$local = Join-Path $root '.local'
$testTemp = Join-Path $local 'front-smoke'
$processTemp = Join-Path $local 'temp'
$src = Join-Path $root 'src'
$project = Join-Path $src 'LogBlade.FrontSmoke\LogBlade.FrontSmoke.csproj'

New-Item -ItemType Directory -Force -Path $testTemp, $processTemp | Out-Null

$env:LOGBLADE_TEST_TEMP = $testTemp
$env:TEMP = $processTemp
$env:TMP = $processTemp

& dotnet run --project $project -c Release --nologo -p:RestoreIgnoreFailedSources=true
if ($LASTEXITCODE -ne 0) {
    throw "LogBlade.FrontSmoke failed with exit code $LASTEXITCODE"
}
