[CmdletBinding()]
param(
    [Alias('w')]
    [switch]$Windows,

    [Alias('a')]
    [switch]$Android,

    [Alias('f')]
    [switch]$Full,

    [Parameter(Position = 0)]
    [string]$AndroidVersion
)

$ErrorActionPreference = 'Stop'
$script:AndroidTargetFramework = 'net10.0-android36.0'
$script:AndroidTargetApiLevel = 36
$script:DefaultAndroidVersion = 10
$script:AndroidApiLevels = @{
    10 = 29
    11 = 30
    12 = 31
    13 = 33
    14 = 34
    15 = 35
    16 = 36
}

function Write-Usage {
    Write-Host 'Usage:'
    Write-Host '  .\publish.ps1 -W           Publish trimmed, self-contained Windows x64'
    Write-Host '  .\publish.ps1 -A           Publish Android APKs supporting Android 10 and later'
    Write-Host '  .\publish.ps1 -A 16        Publish an ARM64 APK requiring Android 16 or later'
    Write-Host '  .\publish.ps1 -F [10-16]   Publish Windows x64 and Android'
    Write-Host ''
    Write-Host 'The Android number selects the minimum installable Android version.'
    Write-Host 'All Android builds compile and target Android 16 (API 36).'
    Write-Host 'Android 10-15 builds produce ARM64 and ARM32 APKs; Android 16 produces ARM64.'
}

function Resolve-AndroidPublishSettings {
    param(
        [string]$RequestedVersion
    )

    if ([string]::IsNullOrWhiteSpace($RequestedVersion)) {
        $version = $script:DefaultAndroidVersion
    }
    else {
        $version = 0
        if (-not [int]::TryParse($RequestedVersion, [ref]$version)) {
            throw [System.FormatException]::new(
                "Invalid Android version '$RequestedVersion'. Enter a whole number from 10 through 16, for example: publish -A 16"
            )
        }
    }

    if (-not $script:AndroidApiLevels.ContainsKey($version)) {
        $supportedVersions = ($script:AndroidApiLevels.Keys | Sort-Object) -join ', '
        throw [System.ArgumentOutOfRangeException]::new(
            'AndroidVersion',
            $version,
            "Unsupported Android version. Supported values are: $supportedVersions."
        )
    }

    [PSCustomObject]@{
        Version = $version
        MinimumApiLevel = $script:AndroidApiLevels[$version]
    }
}

function Invoke-CommandChecked {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Description,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    Write-Host ''
    Write-Host $Description
    Write-Host ('-' * $Description.Length)

    & $Command | Out-Host
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        throw "$Description failed with exit code $exitCode."
    }
}

function Get-VersionSortValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $stablePart = ($Value -split '-', 2)[0]
    try {
        return [version]$stablePart
    }
    catch {
        return [version]'0.0'
    }
}

function Find-AndroidBuildTool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ToolName
    )

    $sdkCandidates = New-Object System.Collections.Generic.List[string]

    foreach ($candidate in @($env:ANDROID_SDK_ROOT, $env:ANDROID_HOME)) {
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            $sdkCandidates.Add($candidate)
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $sdkCandidates.Add((Join-Path $env:LOCALAPPDATA 'Android\Sdk'))
    }

    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $sdkCandidates.Add((Join-Path ${env:ProgramFiles(x86)} 'Android\android-sdk'))
    }

    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $sdkCandidates.Add((Join-Path $env:ProgramFiles 'Android\android-sdk'))
    }

    foreach ($sdkRoot in ($sdkCandidates | Select-Object -Unique)) {
        $buildToolsRoot = Join-Path $sdkRoot 'build-tools'
        if (-not (Test-Path $buildToolsRoot)) {
            continue
        }

        $buildToolVersions = Get-ChildItem -Path $buildToolsRoot -Directory |
            Sort-Object -Property @{ Expression = { Get-VersionSortValue $_.Name } } -Descending

        foreach ($buildToolVersion in $buildToolVersions) {
            $toolPath = Join-Path $buildToolVersion.FullName $ToolName
            if (Test-Path $toolPath) {
                return $toolPath
            }
        }
    }

    return $null
}

function Test-AndroidPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApkPath,

        [Parameter(Mandatory = $true)]
        [int]$ExpectedMinimumApiLevel,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedAbi
    )

    $aapt2 = Find-AndroidBuildTool 'aapt2.exe'
    if (-not $aapt2) {
        Write-Warning 'aapt2.exe was not found. APK manifest and ABI validation was skipped.'
    }
    else {
        $badgingOutput = & $aapt2 dump badging $ApkPath 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "aapt2 could not inspect '$ApkPath'.`n$($badgingOutput -join "`n")"
        }

        $badgingText = $badgingOutput -join "`n"
        $minimumSdkMatch = [regex]::Match($badgingText, "sdkVersion:'(?<value>\d+)'", 'IgnoreCase')
        $targetSdkMatch = [regex]::Match($badgingText, "targetSdkVersion:'(?<value>\d+)'", 'IgnoreCase')
        $nativeCodeMatch = [regex]::Match($badgingText, "native-code:(?<value>[^\r\n]+)", 'IgnoreCase')

        if (-not $minimumSdkMatch.Success) {
            throw "The APK minimum SDK could not be determined: '$ApkPath'."
        }

        $actualMinimumApiLevel = [int]$minimumSdkMatch.Groups['value'].Value
        if ($actualMinimumApiLevel -ne $ExpectedMinimumApiLevel) {
            throw "APK minimum SDK validation failed. Expected API $ExpectedMinimumApiLevel but found API $actualMinimumApiLevel."
        }

        if (-not $targetSdkMatch.Success) {
            throw "The APK target SDK could not be determined: '$ApkPath'."
        }

        $actualTargetApiLevel = [int]$targetSdkMatch.Groups['value'].Value
        if ($actualTargetApiLevel -ne $script:AndroidTargetApiLevel) {
            throw "APK target SDK validation failed. Expected API $($script:AndroidTargetApiLevel) but found API $actualTargetApiLevel."
        }

        if (-not $nativeCodeMatch.Success) {
            throw "The APK does not report any packaged native CPU architectures: '$ApkPath'."
        }

        $nativeCode = $nativeCodeMatch.Groups['value'].Value
        $escapedExpectedAbi = [regex]::Escape($ExpectedAbi)
        if ($nativeCode -notmatch "'$escapedExpectedAbi'") {
            throw "APK CPU architecture validation failed. Expected '$ExpectedAbi'. Reported architectures: $nativeCode"
        }

        foreach ($unexpectedAbi in @('armeabi-v7a', 'arm64-v8a', 'x86', 'x86_64') | Where-Object { $_ -ne $ExpectedAbi }) {
            $escapedUnexpectedAbi = [regex]::Escape($unexpectedAbi)
            if ($nativeCode -match "'$escapedUnexpectedAbi'") {
                throw "APK CPU architecture validation failed. The architecture-specific APK unexpectedly also contains '$unexpectedAbi'. Reported architectures: $nativeCode"
            }
        }

        Write-Host "Verified minimum SDK: API $actualMinimumApiLevel"
        Write-Host "Verified target SDK: API $actualTargetApiLevel"
        Write-Host "Verified CPU architecture: $ExpectedAbi"
    }

    $zipalign = Find-AndroidBuildTool 'zipalign.exe'
    if (-not $zipalign) {
        Write-Warning 'zipalign.exe was not found. APK 16 KB ZIP alignment validation was skipped.'
    }
    else {
        $zipalignOutput = & $zipalign -c -P 16 -v 4 $ApkPath 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "APK 16 KB ZIP alignment validation failed for '$ApkPath'.`n$($zipalignOutput -join "`n")"
        }

        Write-Host 'Verified APK ZIP alignment for 16 KB page-size devices.'
    }
}

function Test-WindowsPublish {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputPath
    )

    $requiredFiles = @(
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
        'coreclr.dll',
        'hostfxr.dll',
        'hostpolicy.dll',
        'Avalonia.Win32.dll',
        'Avalonia.Skia.dll',
        'libSkiaSharp.dll'
    )

    foreach ($requiredFile in $requiredFiles) {
        if (-not (Test-Path (Join-Path $OutputPath $requiredFile))) {
            throw "Windows publish validation failed. Required file is missing: $requiredFile"
        }
    }

    $agentRuntimePath = Join-Path $OutputPath 'AgentRuntime'
    $requiredAgentFiles = @(
        'PasswordManagerLocal.Windows.Agent.exe',
        'PasswordManagerLocal.Windows.Agent.dll',
        'PasswordManagerLocal.Windows.Agent.deps.json',
        'PasswordManagerLocal.Windows.Agent.runtimeconfig.json',
        'PasswordManagerLocal.Common.Backend.dll',
        'PasswordManagerLocal.Common.Backend.Hosting.dll',
        'PasswordManagerLocal.Common.Preferences.dll',
        'PasswordManagerLocal.Common.Contracts.dll',
        'PasswordManagerLocal.Windows.Ipc.dll',
        'PasswordManagerLocal.Windows.Backend.dll',
        'PasswordManagerLocal.Windows.EndpointRpc.Contracts.dll',
        'PasswordManagerLocal.Windows.EndpointRpc.Server.dll'
    )
    foreach ($requiredAgentFile in $requiredAgentFiles) {
        if (-not (Test-Path (Join-Path $agentRuntimePath $requiredAgentFile))) {
            throw "Windows publish validation failed. AgentRuntime file is missing: $requiredAgentFile"
        }
    }

    foreach ($backendOnlyRootFile in @(
        'PasswordManagerLocal.Common.Backend.dll',
        'PasswordManagerLocal.Common.Backend.Hosting.dll',
        'PasswordManagerLocal.Windows.Backend.dll',
        'PasswordManagerLocal.Windows.EndpointRpc.Server.dll',
        'Microsoft.EntityFrameworkCore.dll',
        'Microsoft.EntityFrameworkCore.Relational.dll',
        'Microsoft.EntityFrameworkCore.Sqlite.dll',
        'Microsoft.Data.Sqlite.dll',
        'SQLitePCLRaw.core.dll'
    )) {
        if (Test-Path (Join-Path $OutputPath $backendOnlyRootFile)) {
            throw "Windows publish validation failed. Backend-only file is present in the frontend root: $backendOnlyRootFile"
        }
    }

    $unexpectedFiles = @(
        'Avalonia.Desktop.dll',
        'Avalonia.X11.dll',
        'Avalonia.FreeDesktop.dll',
        'Avalonia.Native.dll',
        'Tmds.DBus.Protocol.dll',
        'Avalonia.DesignerSupport.dll',
        'Avalonia.Remote.Protocol.dll',
        'createdump.exe',
        'mscordaccore.dll',
        'mscordbi.dll',
        'Microsoft.DiaSymReader.Native.amd64.dll'
    )

    foreach ($unexpectedFile in $unexpectedFiles) {
        if (Test-Path (Join-Path $OutputPath $unexpectedFile)) {
            throw "Windows publish validation failed. Excluded release-only asset was published: $unexpectedFile"
        }
    }

    $versionedDacFiles = Get-ChildItem -Path $OutputPath -File -Filter 'mscordaccore_*.dll'
    if ($versionedDacFiles) {
        $dacNames = ($versionedDacFiles.Name | Sort-Object -Unique) -join ', '
        throw "Windows publish validation failed. Crash-dump DAC assets were published: $dacNames"
    }

    $depsPath = Join-Path $OutputPath 'PasswordManagerLocal.deps.json'
    $depsText = Get-Content -LiteralPath $depsPath -Raw
    foreach ($excludedManifestEntry in @(
        'Avalonia.DesignerSupport.dll',
        'Avalonia.Remote.Protocol',
        'createdump.exe',
        'mscordaccore.dll',
        'mscordbi.dll',
        'Microsoft.DiaSymReader.Native.amd64.dll'
    )) {
        if ($depsText.Contains($excludedManifestEntry)) {
            throw "Windows publish validation failed. The dependency manifest still contains excluded asset: $excludedManifestEntry"
        }
    }

    if ($depsText -match 'mscordaccore_[^"\/]+\.dll') {
        throw 'Windows publish validation failed. The dependency manifest still contains a version-qualified crash-dump DAC asset.'
    }

    foreach ($backendOnlyDependency in @(
        'PasswordManagerLocal.Common.Backend',
        'PasswordManagerLocal.Common.Backend.Hosting',
        'PasswordManagerLocal.Windows.Backend',
        'PasswordManagerLocal.Windows.EndpointRpc.Server',
        'Microsoft.EntityFrameworkCore',
        'Microsoft.Data.Sqlite',
        'SQLitePCLRaw'
    )) {
        if ($depsText.Contains($backendOnlyDependency)) {
            throw "Windows publish validation failed. Frontend dependency manifest contains backend-only dependency: $backendOnlyDependency"
        }
    }

    $agentDepsPath = Join-Path $agentRuntimePath 'PasswordManagerLocal.Windows.Agent.deps.json'
    $agentDepsText = Get-Content -LiteralPath $agentDepsPath -Raw
    foreach ($requiredAgentDependency in @(
        'PasswordManagerLocal.Common.Backend',
        'PasswordManagerLocal.Windows.EndpointRpc.Server'
    )) {
        if (-not $agentDepsText.Contains($requiredAgentDependency)) {
            throw "Windows publish validation failed. Agent dependency manifest is missing: $requiredAgentDependency"
        }
    }

    $publishedSymbols = Get-ChildItem -Path $OutputPath -Recurse -File -Filter '*.pdb'
    if ($publishedSymbols) {
        $symbolNames = ($publishedSymbols.Name | Sort-Object -Unique) -join ', '
        throw "Windows publish validation failed. Release symbols were published: $symbolNames"
    }

    $files = Get-ChildItem -Path $OutputPath -Recurse -File
    $totalBytes = ($files | Measure-Object -Property Length -Sum).Sum
    $totalMiB = [math]::Round($totalBytes / 1MB, 2)

    Write-Host "Verified self-contained runtime: coreclr.dll is present"
    Write-Host "Verified Avalonia.Desktop/X11/FreeDesktop/Native and D-Bus are absent"
    Write-Host "Verified Avalonia designer and remote-protocol runtime assets are absent"
    Write-Host "Verified crash-dump and managed-debugger payloads are absent"
    Write-Host "Verified the dependency manifest contains none of the excluded assets"
    if (Test-Path (Join-Path $OutputPath 'Avalonia.Metal.dll')) {
        Write-Host "Avalonia.Metal.dll is present as Avalonia's shared rendering abstraction (allowed)"
    }
    Write-Host "Verified backend-only assemblies are confined to AgentRuntime"
    Write-Host "Verified release output contains no PDB files"
    Write-Host "Published files: $($files.Count), total size: $totalMiB MiB"
}

function Publish-WindowsApp {
    $project = Join-Path $script:Root 'Windows\Frontend\PasswordManagerLocal.Windows.Frontend.csproj'
    $output = Join-Path $script:Root 'artifacts\publish\PasswordManagerLocal.Windows.Frontend\win-x64'

    if (Test-Path $output) {
        Remove-Item -Path $output -Recurse -Force
    }

    Invoke-CommandChecked 'Publishing trimmed self-contained Windows x64 app' {
        dotnet publish $project `
            -c Release `
            -f net10.0-windows `
            -r win-x64 `
            --self-contained true `
            -p:PublishProfile=FolderProfile
    }

    Write-Host ''
    Write-Host 'Validating Windows publish'
    Write-Host '--------------------------'
    Test-WindowsPublish -OutputPath $output
    & (Join-Path $script:Root 'Tools\Windows\VerifyWindowsPublishedLayout.ps1') `
        -PublishDirectory $output

    Write-Host ''
    Write-Host "Windows publish output: $output"
}

function Publish-AndroidArchitecture {
    param(
        [Parameter(Mandatory = $true)]
        [PSCustomObject]$Settings,

        [Parameter(Mandatory = $true)]
        [PSCustomObject]$Architecture,

        [Parameter(Mandatory = $true)]
        [string]$Project,

        [Parameter(Mandatory = $true)]
        [string]$OutputRoot
    )

    $targetBinRoot = Join-Path $script:Root "Android\Frontend\bin\Release\$($script:AndroidTargetFramework)"
    $architectureOutput = Join-Path $OutputRoot $Architecture.Abi

    if (Test-Path $targetBinRoot) {
        Remove-Item -Path $targetBinRoot -Recurse -Force
    }

    if (Test-Path $architectureOutput) {
        Remove-Item -Path $architectureOutput -Recurse -Force
    }

    New-Item -ItemType Directory -Path $architectureOutput -Force | Out-Null

    $buildArguments = @(
        'build',
        $Project,
        '-c', 'Release',
        '-f', $script:AndroidTargetFramework,
        '-r', $Architecture.RuntimeIdentifier,
        '-t:SignAndroidPackage',
        "-p:SupportedOSPlatformVersion=$($Settings.MinimumApiLevel)",
        "-p:AndroidMinimumVersion=$($Settings.Version)",
        "-p:AndroidArtifactsPublishDir=$architectureOutput",
        '-p:WarningsAsErrors=XA0141'
    )

    Invoke-CommandChecked "Publishing Android $($Architecture.Abi) APK" {
        dotnet @buildArguments
    }

    $signedApk = Get-ChildItem -Path $architectureOutput -Recurse -File -Filter '*.apk' |
        Where-Object { $_.Name -match '-signed\.apk$' } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if (-not $signedApk) {
        $signedApk = Get-ChildItem -Path $targetBinRoot -Recurse -File -Filter '*.apk' |
            Where-Object { $_.Name -match '-signed\.apk$' } |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
    }

    if (-not $signedApk) {
        throw "No newly built signed APK was found for $($Architecture.Abi)."
    }

    $finalName = "PasswordManagerLocal-android-$($Settings.Version)-plus-$($Architecture.Abi)-signed.apk"
    $finalPath = Join-Path $OutputRoot $finalName
    Copy-Item -Path $signedApk.FullName -Destination $finalPath -Force

    Write-Host ''
    Write-Host "Validating APK: $finalName"
    Test-AndroidPackage -ApkPath $finalPath -ExpectedMinimumApiLevel $Settings.MinimumApiLevel -ExpectedAbi $Architecture.Abi

    return $finalPath
}

function Publish-AndroidApp {
    param(
        [Parameter(Mandatory = $true)]
        [PSCustomObject]$Settings
    )

    $project = Join-Path $script:Root 'Android\Frontend\PasswordManagerLocal.Android.Frontend.csproj'
    $outputRoot = Join-Path $script:Root "artifacts\publish\PasswordManagerLocal.Android.Frontend\android-$($Settings.Version)-plus"

    if (Test-Path $outputRoot) {
        Remove-Item -Path $outputRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

    $architectures = @(
        [PSCustomObject]@{
            RuntimeIdentifier = 'android-arm64'
            Abi = 'arm64-v8a'
        }
    )

    if ($Settings.Version -le 15) {
        $architectures += [PSCustomObject]@{
            RuntimeIdentifier = 'android-arm'
            Abi = 'armeabi-v7a'
        }
    }

    Write-Host ''
    Write-Host "Android compile/target version: Android 16 (API $($script:AndroidTargetApiLevel))"
    Write-Host "Android minimum install version: Android $($Settings.Version) (API $($Settings.MinimumApiLevel))"
    Write-Host "APK CPU architectures: $(($architectures.Abi) -join ', ')"

    $publishedApks = @()
    foreach ($architecture in $architectures) {
        $publishedApks += Publish-AndroidArchitecture -Settings $Settings -Architecture $architecture -Project $project -OutputRoot $outputRoot
    }

    Write-Host ''
    Write-Host "Android publish output: $outputRoot"
    foreach ($publishedApk in $publishedApks) {
        Write-Host "Signed APK: $(Split-Path -Leaf $publishedApk)"
    }
}

$script:Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $script:Root

$selectedCount = 0
if ($Windows.IsPresent) { $selectedCount++ }
if ($Android.IsPresent) { $selectedCount++ }
if ($Full.IsPresent) { $selectedCount++ }

if ($selectedCount -eq 0) {
    Write-Usage
    exit 1
}

if ($selectedCount -gt 1) {
    Write-Host 'Choose exactly one publish mode: -W, -A, or -F.' -ForegroundColor Red
    Write-Usage
    exit 2
}

if (-not [string]::IsNullOrWhiteSpace($AndroidVersion) -and -not ($Android.IsPresent -or $Full.IsPresent)) {
    Write-Host 'An Android version can only be supplied with -A or -F.' -ForegroundColor Red
    Write-Usage
    exit 2
}

$androidSettings = $null
if ($Android.IsPresent -or $Full.IsPresent) {
    try {
        $androidSettings = Resolve-AndroidPublishSettings -RequestedVersion $AndroidVersion
    }
    catch [System.FormatException] {
        Write-Host $_.Exception.Message -ForegroundColor Red
        exit 2
    }
    catch [System.ArgumentOutOfRangeException] {
        Write-Host $_.Exception.Message -ForegroundColor Red
        exit 3
    }
}

try {
    if ($Full.IsPresent) {
        Publish-WindowsApp
        Publish-AndroidApp -Settings $androidSettings
        exit 0
    }

    if ($Windows.IsPresent) {
        Publish-WindowsApp
    }

    if ($Android.IsPresent) {
        Publish-AndroidApp -Settings $androidSettings
    }
}
catch {
    Write-Host ''
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
