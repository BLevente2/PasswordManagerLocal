[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$RuntimeIdentifier = 'win-x64',

    [switch]$SkipPublishChecks,

    [string]$PublishDirectory,

    [switch]$NoRestore,

    [switch]$KeepTemporaryPublish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$solution = Join-Path $root 'PasswordManagerLocal.sln'
$uiProject = Join-Path $root 'PasswordManagerLocal\PasswordManagerLocal.Windows\PasswordManagerLocal.Windows.csproj'
$documentationVerifier = Join-Path $root 'Tools\VerifyDocumentationLayout.ps1'
$publishVerifier = Join-Path $root 'Tools\Windows\VerifyWindowsPublishedLayout.ps1'
$temporaryPublish = $false
$summary = [System.Collections.Generic.List[string]]::new()

function Stop-Phase9 {
    param(
        [int]$ExitCode,
        [string]$Message
    )

    [Console]::Error.WriteLine($Message)
    exit $ExitCode
}

function Invoke-DotNet {
    param(
        [string[]]$Arguments,
        [int]$FailureExitCode,
        [string]$Description
    )

    Write-Host "==> $Description"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        Stop-Phase9 -ExitCode $FailureExitCode -Message "$Description failed with dotnet exit code $LASTEXITCODE."
    }

    $summary.Add("PASS: $Description")
}

$isWindowsPlatform = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
if (-not $isWindowsPlatform) {
    Stop-Phase9 -ExitCode 10 -Message 'Phase 9 automated Windows checks require Windows.'
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Stop-Phase9 -ExitCode 10 -Message 'The dotnet CLI is unavailable.'
}
if (-not (Test-Path -LiteralPath $solution -PathType Leaf)) {
    Stop-Phase9 -ExitCode 10 -Message "Solution not found: $solution"
}

try {
    Write-Host '==> Documentation layout'
    try {
        & $documentationVerifier
    }
    catch {
        Stop-Phase9 -ExitCode 20 -Message "Documentation verification failed: $($_.Exception.Message)"
    }
    $summary.Add('PASS: Documentation layout')

    $buildArguments = @(
        'build', $solution,
        '--configuration', $Configuration
    )
    if ($NoRestore) {
        $buildArguments += '--no-restore'
    }
    Invoke-DotNet -Arguments $buildArguments -FailureExitCode 30 -Description 'Solution build'

    $testProjects = @(
        'PasswordManagerLocal.Test\PasswordManagerLocal.Test.csproj',
        'PasswordManagerLocal.Windows.Ipc.Test\PasswordManagerLocal.Windows.Ipc.Test.csproj',
        'PasswordManagerLocal.Windows.EndpointRpc.Test\PasswordManagerLocal.Windows.EndpointRpc.Test.csproj'
    )
    foreach ($relativeProject in $testProjects) {
        $project = Join-Path $root $relativeProject
        $testArguments = @(
            'test', $project,
            '--configuration', $Configuration,
            '--no-build',
            '--logger', 'console;verbosity=normal'
        )
        Invoke-DotNet -Arguments $testArguments -FailureExitCode 40 -Description "Tests: $relativeProject"
    }

    if ($SkipPublishChecks) {
        $summary.Add('SKIP: Publish and published-layout verification')
    }
    else {
        if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
            $PublishDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "PasswordManagerLocal.Phase9.$([Guid]::NewGuid().ToString('N'))"
            $temporaryPublish = $true
        }

        $PublishDirectory = [System.IO.Path]::GetFullPath($PublishDirectory)
        New-Item -ItemType Directory -Path $PublishDirectory -Force | Out-Null

        $publishArguments = @(
            'publish', $uiProject,
            '--configuration', $Configuration,
            '--runtime', $RuntimeIdentifier,
            '--self-contained', 'true',
            '--output', $PublishDirectory
        )
        if ($NoRestore) {
            $publishArguments += '--no-restore'
        }
        Invoke-DotNet -Arguments $publishArguments -FailureExitCode 50 -Description 'Windows UI and agent publish'

        Write-Host '==> Published-layout verification'
        try {
            & $publishVerifier `
                -PublishDirectory $PublishDirectory `
                -RuntimeIdentifier $RuntimeIdentifier `
                -RepositoryRoot $root
        }
        catch {
            Stop-Phase9 -ExitCode 60 -Message "Published-layout verification failed: $($_.Exception.Message)"
        }
        $summary.Add('PASS: Published-layout verification')
    }

    Write-Host ''
    Write-Host 'Phase 9 automated check summary'
    $summary | ForEach-Object { Write-Host "  $_" }
    exit 0
}
finally {
    if ($temporaryPublish -and -not $KeepTemporaryPublish -and
        -not [string]::IsNullOrWhiteSpace($PublishDirectory) -and
        (Test-Path -LiteralPath $PublishDirectory)) {
        Remove-Item -LiteralPath $PublishDirectory -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $PublishDirectory) {
            Write-Warning "Temporary publish cleanup was incomplete: $PublishDirectory"
        }
    }
}
