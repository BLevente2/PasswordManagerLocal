[CmdletBinding()]
param(
    [Alias('w')]
    [switch]$Windows,

    [Alias('a')]
    [switch]$Android,

    [Alias('f')]
    [switch]$Full
)

$ErrorActionPreference = 'Stop'

function Write-Usage {
    Write-Host 'Usage:'
    Write-Host '  .\publish.ps1 -w      Publish Windows x64'
    Write-Host '  .\publish.ps1 -A      Publish Android ARM64 APK'
    Write-Host '  .\publish.ps1 -F      Publish Windows x64 and Android ARM64 APK'
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

    & $Command

    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Publish-WindowsApp {
    $project = Join-Path $script:Root 'PasswordManagerLocal\PasswordManagerLocal.Windows\PasswordManagerLocal.Windows.csproj'
    $output = Join-Path $script:Root 'artifacts\publish\PasswordManagerLocal.Windows\win-x64'

    Invoke-CommandChecked 'Publishing Windows x64 app' {
        dotnet publish $project -c Release -f net10.0-windows -r win-x64 -p:PublishProfile=FolderProfile
    }

    Write-Host ''
    Write-Host "Windows publish output: $output"
}

function Publish-AndroidApp {
    $project = Join-Path $script:Root 'PasswordManagerLocal\PasswordManagerLocal.Android\PasswordManagerLocal.Android.csproj'
    $binRoot = Join-Path $script:Root 'PasswordManagerLocal\PasswordManagerLocal.Android\bin'
    $output = Join-Path $script:Root 'artifacts\publish\PasswordManagerLocal.Android\android-arm64-apk'

    Invoke-CommandChecked 'Publishing Android ARM64 APK' {
        dotnet build $project -c Release -f net10.0-android36.0 -r android-arm64 -t:SignAndroidPackage
    }

    New-Item -ItemType Directory -Path $output -Force | Out-Null

    $signedApk = Get-ChildItem -Path $binRoot -Recurse -File -Filter '*.apk' |
        Where-Object { $_.Name -match '-signed\.apk$' } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $signedApk) {
        throw "No signed APK was found under '$binRoot'."
    }

    Copy-Item -Path $signedApk.FullName -Destination $output -Force

    Write-Host ''
    Write-Host "Android publish output: $output"
    Write-Host "Copied signed APK: $($signedApk.Name)"
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

if ($Full.IsPresent) {
    Publish-WindowsApp
    Publish-AndroidApp
    exit 0
}

if ($Windows.IsPresent) {
    Publish-WindowsApp
}

if ($Android.IsPresent) {
    Publish-AndroidApp
}
