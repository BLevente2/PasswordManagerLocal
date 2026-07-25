[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$ManifestPath,
    [string]$ExtractedPackageDirectory
)

$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()

function Assert-Phase11([bool]$Condition, [string]$Message) {
    if (-not $Condition) { $script:failures.Add($Message) }
}

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $RepositoryRoot 'PasswordManagerLocal/PasswordManagerLocal.Android/Properties/AndroidManifest.xml'
}
if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Manifest not found: $ManifestPath"
}

[xml]$manifest = Get-Content -LiteralPath $ManifestPath -Raw
$android = 'http://schemas.android.com/apk/res/android'
$manager = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
$manager.AddNamespace('android', $android)

$permissionNames = @($manifest.manifest.'uses-permission' | ForEach-Object { $_.GetAttribute('name', $android) })
foreach ($permission in @(
    'android.permission.FOREGROUND_SERVICE',
    'android.permission.FOREGROUND_SERVICE_CONNECTED_DEVICE',
    'android.permission.POST_NOTIFICATIONS',
    'android.permission.RECEIVE_BOOT_COMPLETED',
    'android.permission.CHANGE_WIFI_MULTICAST_STATE')) {
    Assert-Phase11 ($permissionNames -contains $permission) "Missing permission: $permission"
}
Assert-Phase11 (-not ($permissionNames -contains 'android.permission.FOREGROUND_SERVICE_DATA_SYNC')) 'Obsolete dataSync foreground-service permission is present.'

$service = $manifest.SelectSingleNode("//service[contains(@android:name, 'PasswordManagerBackgroundService')]", $manager)
Assert-Phase11 ($null -ne $service) 'PasswordManagerBackgroundService is not declared.'
if ($null -ne $service) {
    Assert-Phase11 ($service.GetAttribute('exported', $android) -eq 'false') 'The runtime service must be non-exported.'
    Assert-Phase11 ($service.GetAttribute('foregroundServiceType', $android) -eq 'connectedDevice') 'The runtime service must use connectedDevice foreground-service type.'
    Assert-Phase11 ($service.GetAttribute('stopWithTask', $android) -eq 'false') 'The runtime service must not stop merely because the activity task closes.'
}

$receiver = $manifest.SelectSingleNode("//receiver[contains(@android:name, 'AndroidBackgroundRestorationReceiver')]", $manager)
Assert-Phase11 ($null -ne $receiver) 'AndroidBackgroundRestorationReceiver is not declared.'
if ($null -ne $receiver) {
    Assert-Phase11 ($receiver.GetAttribute('exported', $android) -eq 'false') 'The restoration receiver must be non-exported.'
    Assert-Phase11 ([string]::IsNullOrWhiteSpace($receiver.GetAttribute('directBootAware', $android)) -or $receiver.GetAttribute('directBootAware', $android) -eq 'false') 'The restoration receiver must not advertise Direct Boot support.'
    $actions = @($receiver.SelectNodes('.//action', $manager) | ForEach-Object { $_.GetAttribute('name', $android) })
    foreach ($action in @(
        'android.intent.action.BOOT_COMPLETED',
        'android.intent.action.MY_PACKAGE_REPLACED')) {
        Assert-Phase11 ($actions -contains $action) "Missing restoration action: $action"
    }
    Assert-Phase11 (-not ($actions -contains 'android.intent.action.USER_UNLOCKED')) 'USER_UNLOCKED must not start the service through the manifest receiver.'
    Assert-Phase11 (-not ($actions -contains 'android.intent.action.LOCKED_BOOT_COMPLETED')) 'LOCKED_BOOT_COMPLETED must not be declared for credential-encrypted settings.'
}

$activitySource = Join-Path $RepositoryRoot 'PasswordManagerLocal/PasswordManagerLocal.Android/MainActivity.cs'
$controllerSource = Join-Path $RepositoryRoot 'PasswordManagerLocal/PasswordManagerLocal.Android/Runtime/AndroidForegroundServiceController.cs'
$serviceSource = Join-Path $RepositoryRoot 'PasswordManagerLocal/PasswordManagerLocal.Android/Runtime/PasswordManagerBackgroundService.cs'
$connectorSource = Join-Path $RepositoryRoot 'PasswordManagerLocal/PasswordManagerLocal.Android/Runtime/AndroidRuntimeServiceConnector.cs'
$runtimeHostSource = Join-Path $RepositoryRoot 'PasswordManagerLocal.Android.Runtime/AndroidRuntimeServiceHost.cs'
$receiverSource = Join-Path $RepositoryRoot 'PasswordManagerLocal/PasswordManagerLocal.Android/Background/AndroidBackgroundRestorationReceiver.cs'
$unlockReceiverSource = Join-Path $RepositoryRoot 'PasswordManagerLocal/PasswordManagerLocal.Android/Background/AndroidDeferredUnlockReceiver.cs'
$iconPath = Join-Path $RepositoryRoot 'PasswordManagerLocal/PasswordManagerLocal.Android/Resources/drawable/ic_stat_password_manager.xml'
$obsoleteUnlockReceiver = Join-Path $RepositoryRoot 'PasswordManagerLocal/PasswordManagerLocal.Android/Background/AndroidUserUnlockedReceiver.cs'

foreach ($path in @($activitySource, $controllerSource, $serviceSource, $connectorSource, $runtimeHostSource, $receiverSource, $unlockReceiverSource, $iconPath)) {
    Assert-Phase11 (Test-Path -LiteralPath $path -PathType Leaf) "Required Android source/resource is missing: $path"
}
Assert-Phase11 (-not (Test-Path -LiteralPath $obsoleteUnlockReceiver)) 'The obsolete dynamic user-unlock receiver still exists.'
if (Test-Path -LiteralPath $unlockReceiverSource) {
    $unlockReceiver = Get-Content -LiteralPath $unlockReceiverSource -Raw
    Assert-Phase11 ($unlockReceiver.Contains('Intent.ActionUserUnlocked')) 'The deferred unlock receiver does not handle USER_UNLOCKED.'
    Assert-Phase11 ($unlockReceiver.Contains('_queueRestoration();')) 'The deferred unlock receiver does not queue the service restoration pipeline.'
    Assert-Phase11 (-not $unlockReceiver.Contains('StartForegroundService')) 'The deferred unlock receiver must not start the service from the background.'
    Assert-Phase11 (-not $unlockReceiver.Contains('BackgroundSyncSettingsStore')) 'The deferred unlock receiver must not read persisted settings.'
}
if (Test-Path -LiteralPath $serviceSource) {
    $serviceSourceText = Get-Content -LiteralPath $serviceSource -Raw
    Assert-Phase11 ($serviceSourceText.Contains('new AndroidDeferredUnlockReceiver(QueueBackgroundRestoration)')) 'The service does not route USER_UNLOCKED through its one restoration queue.'
    Assert-Phase11 ($serviceSourceText.Contains('ReceiverFlags.NotExported')) 'The deferred unlock receiver is not registered as non-exported on supported Android versions.'
    Assert-Phase11 ($serviceSourceText.Contains('UnregisterDeferredUnlockReceiver')) 'The deferred unlock receiver is not unregistered during service cleanup.'
}
if (Test-Path -LiteralPath $controllerSource) {
    $controller = Get-Content -LiteralPath $controllerSource -Raw
    Assert-Phase11 ($controller.Contains('typeof(MainActivity)')) 'Notification pending intent is not explicitly targeted at MainActivity.'
    Assert-Phase11 ($controller.Contains('PendingIntentFlags.Immutable')) 'Notification pending intent is not immutable.'
    Assert-Phase11 ($controller.Contains('Resource.Drawable.ic_stat_password_manager')) 'Foreground notification does not use the dedicated status icon.'
    Assert-Phase11 (-not $controller.Contains('PutExtra(')) 'Notification launch intent must not carry extras.'
}
if (Test-Path -LiteralPath $activitySource) {
    $activity = Get-Content -LiteralPath $activitySource -Raw
    Assert-Phase11 ($activity.Contains('MainLauncher = true')) 'MainActivity is not declared as the launcher activity in source.'
    Assert-Phase11 ($activity.Contains('Exported = true')) 'MainActivity does not explicitly declare launcher export state.'
    Assert-Phase11 (-not $activity.Contains('AndroidBackendRuntimeFactory')) 'MainActivity contains a runtime-factory reference.'
    Assert-Phase11 (-not $activity.Contains('new BackendRuntime')) 'MainActivity contains a runtime fallback.'
}
if (Test-Path -LiteralPath $connectorSource) {
    $connector = Get-Content -LiteralPath $connectorSource -Raw
    Assert-Phase11 ($connector.Contains('connection.WaitForServiceAsync(timeoutSource.Token)')) 'The activity initialization timeout does not cover service binding.'
    Assert-Phase11 ($connector.Contains('service.AttachInteractiveClientAsync(timeoutSource.Token)')) 'The activity initialization timeout does not cover runtime attachment.'
    Assert-Phase11 (-not $connector.Contains('service.AttachInteractiveClientAsync(cancellationToken)')) 'Runtime attachment bypasses the bounded initialization timeout.'
}
if (Test-Path -LiteralPath $runtimeHostSource) {
    $runtimeHost = Get-Content -LiteralPath $runtimeHostSource -Raw
    $attachIndex = $runtimeHost.IndexOf('AttachInteractiveClientAsync')
    $attachUnlockIndex = $runtimeHost.IndexOf('!_secureStorageAvailability.IsAvailable', $attachIndex)
    $attachSettingReadIndex = $runtimeHost.IndexOf('LoadSettingsLockedAsync', $attachIndex)
    Assert-Phase11 ($attachUnlockIndex -gt $attachIndex -and $attachSettingReadIndex -gt $attachUnlockIndex) 'Interactive attachment can read settings before secure storage is available.'
    $settingMutationIndex = $runtimeHost.IndexOf('SetBackgroundEnabledCoreAsync')
    $mutationUnlockIndex = $runtimeHost.IndexOf('!_secureStorageAvailability.IsAvailable', $settingMutationIndex)
    $mutationSettingReadIndex = $runtimeHost.IndexOf('LoadSettingsLockedAsync', $settingMutationIndex)
    Assert-Phase11 ($mutationUnlockIndex -gt $settingMutationIndex -and $mutationSettingReadIndex -gt $mutationUnlockIndex) 'Background-setting mutation can read/write before secure storage is available.'
    $resetIndex = $runtimeHost.IndexOf('ResetDatabaseAsync')
    $resetCloseIndex = $runtimeHost.IndexOf('CloseFromHostAsync', $resetIndex)
    $resetBackgroundRestoreIndex = $runtimeHost.IndexOf('EnsureBackgroundRuntimeLockedAsync(cancellationToken)', $resetCloseIndex)
    Assert-Phase11 ($resetBackgroundRestoreIndex -gt $resetCloseIndex) 'Database reset can restore BackgroundSync without re-establishing foreground legality.'
}

$androidProductionRoots = @(
    (Join-Path $RepositoryRoot 'PasswordManagerLocal/PasswordManagerLocal.Android'),
    (Join-Path $RepositoryRoot 'PasswordManagerLocal.Android.Runtime'),
    (Join-Path $RepositoryRoot 'PasswordManagerLocal.Backend.Android'))
$factoryCallers = @()
foreach ($root in $androidProductionRoots) {
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
    foreach ($sourceFile in Get-ChildItem -LiteralPath $root -Filter '*.cs' -Recurse -File) {
        if ($sourceFile.FullName -match '[\\/](bin|obj)[\\/]') { continue }
        $sourceText = Get-Content -LiteralPath $sourceFile.FullName -Raw
        if ($sourceText.Contains('AndroidBackendRuntimeFactory.Create(')) {
            $factoryCallers += $sourceFile.FullName
        }
    }
}
Assert-Phase11 ($factoryCallers.Count -eq 1) "Expected one Android runtime-factory caller; found $($factoryCallers.Count)."
if ($factoryCallers.Count -eq 1) {
    Assert-Phase11 ($factoryCallers[0] -eq (Resolve-Path -LiteralPath $serviceSource).Path) `
        'PasswordManagerBackgroundService is not the sole Android runtime-factory caller.'
}

if (-not [string]::IsNullOrWhiteSpace($ExtractedPackageDirectory)) {
    if (-not (Test-Path -LiteralPath $ExtractedPackageDirectory -PathType Container)) {
        throw "Extracted package directory not found: $ExtractedPackageDirectory"
    }
    $relativeFiles = @(Get-ChildItem -LiteralPath $ExtractedPackageDirectory -Recurse -File | ForEach-Object {
        [IO.Path]::GetRelativePath($ExtractedPackageDirectory, $_.FullName).Replace('\\', '/')
    })
    Assert-Phase11 (($relativeFiles | Where-Object { $_ -match '(^|/)ic_stat_password_manager\.(xml|png)$' }).Count -gt 0) 'Extracted package does not contain the notification icon resource.'
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}
Write-Host 'Android manifest, resources, pending-intent assumptions, and runtime ownership guards passed.'
exit 0
