$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'PasswordManagerLocal\PasswordManagerLocal.Windows\PasswordManagerLocal.Windows.csproj'
$output = Join-Path $root 'artifacts\publish\PasswordManagerLocal.Windows\win-x64'

if (Test-Path $output) {
    Remove-Item $output -Recurse -Force
}

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    -p:PublishReadyToRun=false `
    -p:PublishSingleFile=false `
    -o $output
