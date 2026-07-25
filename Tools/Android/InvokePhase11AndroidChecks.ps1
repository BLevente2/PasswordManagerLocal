[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$PackageName,
    [Parameter(Mandatory)] [string]$ActivityName,
    [string]$ApkPath,
    [string]$AdbPath = 'adb',
    [string]$DeviceSerial,
    [string]$ResultsDirectory = (Join-Path $PWD ('phase11-android-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))),
    [ValidateRange(5, 120)] [int]$TimeoutSeconds = 30,
    [switch]$SendRestorationBroadcasts,
    [switch]$ForceStopAndRelaunch
)

$ErrorActionPreference = 'Stop'
if ($PackageName -notmatch '^[A-Za-z0-9_.]+$') { throw 'PackageName contains unsupported characters.' }
if ([string]::IsNullOrWhiteSpace($ActivityName)) { throw 'ActivityName is required.' }
if (-not [string]::IsNullOrWhiteSpace($ApkPath) -and -not (Test-Path -LiteralPath $ApkPath -PathType Leaf)) { throw "APK not found: $ApkPath" }
$adb = (Get-Command $AdbPath -ErrorAction Stop).Source
New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null

$serialArgs = @()
if (-not [string]::IsNullOrWhiteSpace($DeviceSerial)) { $serialArgs = @('-s', $DeviceSerial) }
function Invoke-Adb([string[]]$Arguments, [switch]$AllowFailure, [switch]$WithoutSerial) {
    $prefix = if ($WithoutSerial) { @() } else { @($serialArgs) }
    $allArguments = @($prefix) + @($Arguments)
    $job = Start-Job -ScriptBlock {
        param($adbPath, $commandArguments)
        & $adbPath @commandArguments 2>&1
        [pscustomobject]@{ ExitCode = $LASTEXITCODE }
    } -ArgumentList $adb, (, $allArguments)
    try {
        if (-not (Wait-Job -Job $job -Timeout $TimeoutSeconds)) {
            Stop-Job -Job $job -ErrorAction SilentlyContinue
            throw "ADB command timed out after $TimeoutSeconds seconds: $($Arguments -join ' ')"
        }
        $result = @(Receive-Job -Job $job)
        $exit = ($result | Where-Object { $_ -is [pscustomobject] -and $_.PSObject.Properties.Name -contains 'ExitCode' } | Select-Object -Last 1).ExitCode
        $text = @($result | Where-Object { -not ($_ -is [pscustomobject] -and $_.PSObject.Properties.Name -contains 'ExitCode') }) -join "`n"
        if ($exit -ne 0 -and -not $AllowFailure) { throw "ADB failed ($exit): $($Arguments -join ' ')`n$text" }
        return $text
    } finally {
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    }
}

$devices = Invoke-Adb @('devices') -WithoutSerial
$devices | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'devices.txt')
$connected = @($devices -split "`r?`n" | Where-Object { $_ -match "\tdevice$" })
if ([string]::IsNullOrWhiteSpace($DeviceSerial) -and $connected.Count -ne 1) {
    throw 'Specify -DeviceSerial unless exactly one ready device is connected.'
}

if (-not [string]::IsNullOrWhiteSpace($ApkPath)) {
    Invoke-Adb @('install', '-r', (Resolve-Path -LiteralPath $ApkPath).Path) | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'install.txt')
}
Invoke-Adb @('logcat', '-c') | Out-Null
Invoke-Adb @('shell', 'am', 'start', '-W', '-n', "$PackageName/$ActivityName") | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'launch.txt')
Start-Sleep -Seconds 2
Invoke-Adb @('shell', 'dumpsys', 'activity', 'services', $PackageName) -AllowFailure | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'services-after-launch.txt')
Invoke-Adb @('shell', 'pidof', $PackageName) -AllowFailure | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'pid-after-launch.txt')
Invoke-Adb @('shell', 'dumpsys', 'notification') -AllowFailure | Select-String -SimpleMatch $PackageName | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'notification-lines.txt')

if ($SendRestorationBroadcasts) {
    foreach ($action in @('android.intent.action.MY_PACKAGE_REPLACED', 'android.intent.action.USER_UNLOCKED')) {
        Invoke-Adb @('shell', 'am', 'broadcast', '-a', $action, '-p', $PackageName) -AllowFailure |
            Set-Content -LiteralPath (Join-Path $ResultsDirectory (($action -replace '[^A-Za-z0-9]', '_') + '.txt'))
    }
}
if ($ForceStopAndRelaunch) {
    Invoke-Adb @('shell', 'am', 'force-stop', $PackageName) | Out-Null
    Invoke-Adb @('shell', 'pidof', $PackageName) -AllowFailure | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'pid-after-force-stop.txt')
    Invoke-Adb @('shell', 'am', 'start', '-W', '-n', "$PackageName/$ActivityName") | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'relaunch.txt')
}

Invoke-Adb @('logcat', '-d', '-v', 'threadtime') -AllowFailure |
    Select-String -Pattern 'PasswordManagerLocal|PasswordManagerBackgroundService|AndroidRuntimeServiceHost' |
    Set-Content -LiteralPath (Join-Path $ResultsDirectory 'filtered-logcat.txt')

@"
Phase 11 ADB collection completed.
No UI coordinates were used.
Checks still requiring user action: notification permission changes, channel disabling, rotation/recreation, enrollment, database reset, Doze/App Standby, reboot/first unlock, network switching, and OEM battery controls.
Results: $ResultsDirectory
"@ | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'README.txt')
Write-Host "Phase 11 Android diagnostics collected in: $ResultsDirectory"
exit 0
