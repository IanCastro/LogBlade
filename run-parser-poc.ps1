$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root 'artifacts\parser-poc\LogParserPoc.exe'

if (-not (Test-Path $exe)) {
    & (Join-Path $root 'build-parser-poc.ps1')
}

Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
