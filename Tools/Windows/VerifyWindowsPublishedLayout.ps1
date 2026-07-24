[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [string]$RuntimeIdentifier = 'win-x64',

    [string]$StartupCommand,

    [string]$RepositoryRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
)

$ErrorActionPreference = 'Stop'

function Fail-Layout {
    param([string]$Message)
    throw "Windows publish-layout verification failed: $Message"
}

$publish = [System.IO.Path]::TrimEndingDirectorySeparator(
    [System.IO.Path]::GetFullPath($PublishDirectory))
if (-not (Test-Path -LiteralPath $publish -PathType Container)) {
    Fail-Layout "Publish directory does not exist: $publish"
}

$requiredFiles = @(
    'PasswordManagerLocal.Windows.exe',
    'PasswordManagerLocal.Windows.dll',
    'PasswordManagerLocal.Windows.Agent.exe',
    'PasswordManagerLocal.Windows.Agent.dll',
    'PasswordManagerLocal.Windows.deps.json',
    'PasswordManagerLocal.Windows.runtimeconfig.json',
    'PasswordManagerLocal.Windows.Agent.deps.json',
    'PasswordManagerLocal.Windows.Agent.runtimeconfig.json',
    'PasswordManagerLocal.Frontend.dll',
    'PasswordManagerLocal.Backend.dll',
    'PasswordManagerLocal.Backend.Hosting.dll',
    'PasswordManagerLocal.Backend.Windows.dll',
    'PasswordManagerLocal.Runtime.Abstractions.dll',
    'PasswordManagerLocal.Windows.Ipc.dll',
    'PasswordManagerLocal.Windows.EndpointRpc.dll',
    'Microsoft.EntityFrameworkCore.dll',
    'Microsoft.EntityFrameworkCore.Relational.dll',
    'Microsoft.EntityFrameworkCore.Sqlite.dll',
    'Microsoft.Data.Sqlite.dll',
    'SQLitePCLRaw.core.dll',
    'ConfigureWindowsFirewall.ps1',
    'ConfigureWindowsFirewall.bat'
)

$missing = @(
    foreach ($name in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $publish $name) -PathType Leaf)) {
            $name
        }
    }
)
if ($missing.Count -ne 0) {
    Fail-Layout "Missing required artifacts:$([Environment]::NewLine)$($missing -join [Environment]::NewLine)"
}

foreach ($executableName in @(
    'PasswordManagerLocal.Windows.exe',
    'PasswordManagerLocal.Windows.Agent.exe'
)) {
    $executableMatches = @(Get-ChildItem -LiteralPath $publish -Recurse -File -Filter $executableName)
    if ($executableMatches.Count -ne 1 -or $executableMatches[0].DirectoryName -ne $publish) {
        Fail-Layout "Expected exactly one root-level $executableName but found: $($executableMatches.FullName -join ', ')"
    }
}

$trayAsset = Join-Path $publish 'Assets\app_icon.ico'
if (-not (Test-Path -LiteralPath $trayAsset -PathType Leaf)) {
    Fail-Layout 'The tray icon Assets\app_icon.ico is missing.'
}

$nativeCandidates = @(
    (Join-Path $publish 'e_sqlcipher.dll'),
    (Join-Path $publish 'sqlite3.dll'),
    (Join-Path $publish "runtimes\$RuntimeIdentifier\native\e_sqlcipher.dll"),
    (Join-Path $publish "runtimes\$RuntimeIdentifier\native\sqlite3.dll")
)
$existingNativeLibraries = @($nativeCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
if ($existingNativeLibraries.Count -eq 0) {
    Fail-Layout "No SQLite/SQLCipher native library was found for $RuntimeIdentifier. Checked: $($nativeCandidates -join ', ')"
}

$forbiddenFiles = @(
    Get-ChildItem -LiteralPath $publish -Recurse -File |
        Where-Object {
            $_.Name -match '(?i)(\.Test(s)?\.dll$|TestHost.*\.(exe|dll)$|Microsoft\.TestPlatform|testhost\.)'
        }
)
if ($forbiddenFiles.Count -ne 0) {
    Fail-Layout "Test-only files are present in production output:$([Environment]::NewLine)$($forbiddenFiles.FullName -join [Environment]::NewLine)"
}

$nestedBuildDirectories = @(
    Get-ChildItem -LiteralPath $publish -Recurse -Directory |
        Where-Object { $_.Name -in @('bin', 'obj') }
)
if ($nestedBuildDirectories.Count -ne 0) {
    Fail-Layout "Accidental bin/obj nesting is present:$([Environment]::NewLine)$($nestedBuildDirectories.FullName -join [Environment]::NewLine)"
}

$agentPath = [System.IO.Path]::GetFullPath((Join-Path $publish 'PasswordManagerLocal.Windows.Agent.exe'))
$expectedStartupCommand = '"{0}" --background' -f $agentPath
if ([string]::IsNullOrWhiteSpace($StartupCommand)) {
    $StartupCommand = $expectedStartupCommand
}
if ($StartupCommand -cne $expectedStartupCommand) {
    Fail-Layout "Startup command mismatch. Expected '$expectedStartupCommand' but received '$StartupCommand'."
}
if ([regex]::Matches($StartupCommand, '(?i)(?<!\S)--background(?!\S)').Count -ne 1) {
    Fail-Layout 'The startup command must contain --background exactly once.'
}
if ($StartupCommand -match '(?i)PasswordManagerLocal\.Windows\.exe') {
    Fail-Layout 'The startup command points to the UI executable instead of the agent.'
}

$frontendProject = Join-Path $RepositoryRoot 'PasswordManagerLocal\PasswordManagerLocal.Frontend\PasswordManagerLocal.Frontend.csproj'
$localizationDirectory = Join-Path $RepositoryRoot 'PasswordManagerLocal\PasswordManagerLocal.Frontend\Assets\Localization'
if (-not (Test-Path -LiteralPath $frontendProject -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $localizationDirectory 'en_us.json') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $localizationDirectory 'hu.json') -PathType Leaf)) {
    Fail-Layout 'The embedded frontend localization source assets are incomplete.'
}
$frontendProjectText = Get-Content -LiteralPath $frontendProject -Raw
if (-not $frontendProjectText.Contains('<AvaloniaResource Include="Assets\**"')) {
    Fail-Layout 'Frontend assets are not configured as embedded Avalonia resources.'
}

Write-Host "Windows published layout is valid: $publish"
Write-Host "Validated startup command: $expectedStartupCommand"
