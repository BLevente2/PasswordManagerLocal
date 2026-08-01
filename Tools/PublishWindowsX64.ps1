[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Windows\Frontend\PasswordManagerLocal.Windows.Frontend.csproj'
$verifier = Join-Path $root 'Tools\Windows\VerifyWindowsPublishedLayout.ps1'
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'artifacts\publish\PasswordManagerLocal.Windows.Frontend\win-x64'
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The dotnet CLI is unavailable.'
}

$publishArguments = @(
    'publish', $project,
    '--configuration', $Configuration,
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--output', $OutputPath
)
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "Windows publish failed with exit code $LASTEXITCODE."
}

& $verifier -PublishDirectory $OutputPath -RuntimeIdentifier 'win-x64' -RepositoryRoot $root
Write-Host "Windows x64 publish completed: $OutputPath"
