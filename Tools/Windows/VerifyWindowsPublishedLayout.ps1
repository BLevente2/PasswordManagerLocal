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

function Assert-RequiredFiles {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory,

        [Parameter(Mandatory = $true)]
        [string[]]$Names,

        [Parameter(Mandatory = $true)]
        [string]$Scope
    )

    $missing = @(
        foreach ($name in $Names) {
            if (-not (Test-Path -LiteralPath (Join-Path $Directory $name) -PathType Leaf)) {
                $name
            }
        }
    )

    if ($missing.Count -ne 0) {
        Fail-Layout "$Scope is missing required artifacts:$([Environment]::NewLine)$($missing -join [Environment]::NewLine)"
    }
}

$publish = [System.IO.Path]::TrimEndingDirectorySeparator(
    [System.IO.Path]::GetFullPath($PublishDirectory))
if (-not (Test-Path -LiteralPath $publish -PathType Container)) {
    Fail-Layout "Publish directory does not exist: $publish"
}

$agentRuntime = Join-Path $publish 'AgentRuntime'
if (-not (Test-Path -LiteralPath $agentRuntime -PathType Container)) {
    Fail-Layout "The AgentRuntime directory is missing: $agentRuntime"
}

Assert-RequiredFiles -Directory $publish -Scope 'The frontend publish root' -Names @(
    'PasswordManagerLocal.exe',
    'PasswordManagerLocal.dll',
    'PasswordManagerLocal.deps.json',
    'PasswordManagerLocal.runtimeconfig.json',
    'PasswordManagerLocal.Common.Frontend.dll',
    'PasswordManagerLocal.Common.Contracts.dll',
    'PasswordManagerLocal.Common.Preferences.dll',
    'PasswordManagerLocal.Windows.Ipc.dll',
    'PasswordManagerLocal.Windows.EndpointRpc.Contracts.dll',
    'PasswordManagerLocal.Windows.EndpointRpc.Client.dll',
    'ConfigureWindowsFirewall.ps1',
    'ConfigureWindowsFirewall.bat'
)

Assert-RequiredFiles -Directory $agentRuntime -Scope 'The AgentRuntime publish' -Names @(
    'PasswordManagerLocal.Windows.Agent.exe',
    'PasswordManagerLocal.Windows.Agent.dll',
    'PasswordManagerLocal.Windows.Agent.deps.json',
    'PasswordManagerLocal.Windows.Agent.runtimeconfig.json',
    'PasswordManagerLocal.Common.Backend.dll',
    'PasswordManagerLocal.Common.Backend.Hosting.dll',
    'PasswordManagerLocal.Windows.Backend.dll',
    'PasswordManagerLocal.Common.Contracts.dll',
    'PasswordManagerLocal.Common.Preferences.dll',
    'PasswordManagerLocal.Windows.Ipc.dll',
    'PasswordManagerLocal.Windows.EndpointRpc.Contracts.dll',
    'PasswordManagerLocal.Windows.EndpointRpc.Server.dll',
    'Microsoft.EntityFrameworkCore.dll',
    'Microsoft.EntityFrameworkCore.Relational.dll',
    'Microsoft.EntityFrameworkCore.Sqlite.dll',
    'Microsoft.Data.Sqlite.dll',
    'SQLitePCLRaw.core.dll'
)

$forbiddenFrontendFiles = @(
    'PasswordManagerLocal.Windows.Frontend.exe',
    'PasswordManagerLocal.Windows.exe',
    'PasswordManagerLocal.Windows.Agent.exe',
    'PasswordManagerLocal.Windows.Agent.dll',
    'PasswordManagerLocal.Common.Backend.dll',
    'PasswordManagerLocal.Common.Backend.Hosting.dll',
    'PasswordManagerLocal.Windows.Backend.dll',
    'PasswordManagerLocal.Windows.EndpointRpc.Server.dll',
    'Microsoft.EntityFrameworkCore.dll',
    'Microsoft.EntityFrameworkCore.Relational.dll',
    'Microsoft.EntityFrameworkCore.Sqlite.dll',
    'Microsoft.Data.Sqlite.dll',
    'SQLitePCLRaw.core.dll'
)
foreach ($name in $forbiddenFrontendFiles) {
    if (Test-Path -LiteralPath (Join-Path $publish $name) -PathType Leaf) {
        Fail-Layout "Backend/Agent-only artifact is present in the frontend publish root: $name"
    }
}

$frontendExecutableMatches = @(Get-ChildItem -LiteralPath $publish -Recurse -File -Filter 'PasswordManagerLocal.exe')
if ($frontendExecutableMatches.Count -ne 1 -or $frontendExecutableMatches[0].DirectoryName -ne $publish) {
    Fail-Layout "Expected exactly one root-level PasswordManagerLocal.exe but found: $($frontendExecutableMatches.FullName -join ', ')"
}

$agentExecutableMatches = @(Get-ChildItem -LiteralPath $publish -Recurse -File -Filter 'PasswordManagerLocal.Windows.Agent.exe')
if ($agentExecutableMatches.Count -ne 1 -or $agentExecutableMatches[0].DirectoryName -ne $agentRuntime) {
    Fail-Layout "Expected exactly one AgentRuntime-level PasswordManagerLocal.Windows.Agent.exe but found: $($agentExecutableMatches.FullName -join ', ')"
}

$trayAsset = Join-Path $agentRuntime 'Assets\app_icon.ico'
if (-not (Test-Path -LiteralPath $trayAsset -PathType Leaf)) {
    Fail-Layout 'The AgentRuntime tray icon Assets\app_icon.ico is missing.'
}

$nativeCandidates = @(
    (Join-Path $agentRuntime 'e_sqlcipher.dll'),
    (Join-Path $agentRuntime 'sqlite3.dll'),
    (Join-Path $agentRuntime "runtimes\$RuntimeIdentifier\native\e_sqlcipher.dll"),
    (Join-Path $agentRuntime "runtimes\$RuntimeIdentifier\native\sqlite3.dll")
)
$existingNativeLibraries = @($nativeCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
if ($existingNativeLibraries.Count -eq 0) {
    Fail-Layout "No AgentRuntime SQLite/SQLCipher native library was found for $RuntimeIdentifier. Checked: $($nativeCandidates -join ', ')"
}

$frontendDepsPath = Join-Path $publish 'PasswordManagerLocal.deps.json'
$frontendDepsText = Get-Content -LiteralPath $frontendDepsPath -Raw
foreach ($backendOnlyDependency in @(
    'PasswordManagerLocal.Common.Backend',
    'PasswordManagerLocal.Common.Backend.Hosting',
    'PasswordManagerLocal.Windows.Backend',
    'PasswordManagerLocal.Windows.EndpointRpc.Server',
    'Microsoft.EntityFrameworkCore',
    'Microsoft.Data.Sqlite',
    'SQLitePCLRaw'
)) {
    if ($frontendDepsText.Contains($backendOnlyDependency)) {
        Fail-Layout "The frontend dependency manifest contains an Agent/backend-only dependency: $backendOnlyDependency"
    }
}

$agentDepsPath = Join-Path $agentRuntime 'PasswordManagerLocal.Windows.Agent.deps.json'
$agentDepsText = Get-Content -LiteralPath $agentDepsPath -Raw
foreach ($requiredAgentDependency in @(
    'PasswordManagerLocal.Common.Backend',
    'PasswordManagerLocal.Windows.EndpointRpc.Server'
)) {
    if (-not $agentDepsText.Contains($requiredAgentDependency)) {
        Fail-Layout "The Agent dependency manifest is missing: $requiredAgentDependency"
    }
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

$agentPath = [System.IO.Path]::GetFullPath((Join-Path $agentRuntime 'PasswordManagerLocal.Windows.Agent.exe'))
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

$frontendProject = Join-Path $RepositoryRoot 'Common\Frontend\PasswordManagerLocal.Common.Frontend.csproj'
$localizationDirectory = Join-Path $RepositoryRoot 'Common\Frontend\Assets\Localization'
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
Write-Host 'Verified that backend-only assemblies are confined to AgentRuntime.'
Write-Host "Validated startup command: $expectedStartupCommand"
