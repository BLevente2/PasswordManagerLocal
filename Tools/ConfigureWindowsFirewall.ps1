param([string]$AppExe = '')

$ErrorActionPreference = 'Stop'

$tcpAppRule = 'PasswordManagerLocal Sync TCP App'
$tcpPortRule = 'PasswordManagerLocal Sync TCP Port'
$mdnsAppRule = 'PasswordManagerLocal mDNS UDP App'
$mdnsPortRule = 'PasswordManagerLocal mDNS UDP Port'
$legacyRuleNames = @('PasswordManagerLocal Sync TCP', 'PasswordManagerLocal mDNS UDP')
$syncPort = 26688
$mdnsPort = 5353

Import-Module NetSecurity -ErrorAction Stop

function Normalize-PmlPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ''
    }

    try {
        return [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($Path)).TrimEnd('\')
    } catch {
        return [Environment]::ExpandEnvironmentVariables($Path).TrimEnd('\')
    }
}

function Test-PmlProgramMatch {
    param(
        [string]$RuleProgram,
        [string]$AppExe
    )

    if ([string]::IsNullOrWhiteSpace($RuleProgram) -or [string]::IsNullOrWhiteSpace($AppExe)) {
        return $false
    }

    $program = Normalize-PmlPath $RuleProgram
    $app = Normalize-PmlPath $AppExe

    return [string]::Equals($program, $app, [System.StringComparison]::OrdinalIgnoreCase)
}

function Remove-PmlFirewallRuleByDisplayName {
    param([string]$DisplayName)

    Get-NetFirewallRule -DisplayName $DisplayName -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
}

function Remove-PmlConflictingAppBlockRules {
    param([string]$AppExe)

    if ([string]::IsNullOrWhiteSpace($AppExe) -or -not (Test-Path -LiteralPath $AppExe)) {
        return
    }

    $rules = @(Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object {
        ([string]$_.Direction) -ieq 'Inbound' -and
        ([string]$_.Action) -ieq 'Block'
    })

    foreach ($rule in $rules) {
        $filters = @($rule | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue)
        foreach ($filter in $filters) {
            if (Test-PmlProgramMatch -RuleProgram ([string]$filter.Program) -AppExe $AppExe) {
                Write-Host "Removing conflicting app block rule: $($rule.DisplayName)"
                $rule | Remove-NetFirewallRule -ErrorAction SilentlyContinue
                break
            }
        }
    }
}

if ([string]::IsNullOrWhiteSpace($AppExe)) {
    $AppExe = Join-Path $PSScriptRoot 'PasswordManagerLocal.Windows.exe'
}

Write-Host 'Configuring Windows Firewall for PasswordManagerLocal local sync.'
Write-Host "Application path: $AppExe"
Write-Host

@($tcpAppRule, $tcpPortRule, $mdnsAppRule, $mdnsPortRule) + $legacyRuleNames | ForEach-Object {
    Remove-PmlFirewallRuleByDisplayName -DisplayName $_
}

Remove-PmlConflictingAppBlockRules -AppExe $AppExe

New-NetFirewallRule -DisplayName $tcpPortRule -Direction Inbound -Action Allow -Enabled True -Profile Any -Protocol TCP -LocalPort $syncPort -RemoteAddress Any -InterfaceType Any -ErrorAction Stop | Out-Null
New-NetFirewallRule -DisplayName $mdnsPortRule -Direction Inbound -Action Allow -Enabled True -Profile Any -Protocol UDP -LocalPort $mdnsPort -RemoteAddress Any -InterfaceType Any -ErrorAction Stop | Out-Null

if (-not [string]::IsNullOrWhiteSpace($AppExe) -and (Test-Path -LiteralPath $AppExe)) {
    New-NetFirewallRule -DisplayName $tcpAppRule -Direction Inbound -Action Allow -Enabled True -Profile Any -Program $AppExe -Protocol TCP -LocalPort $syncPort -RemoteAddress Any -InterfaceType Any -ErrorAction Stop | Out-Null
    New-NetFirewallRule -DisplayName $mdnsAppRule -Direction Inbound -Action Allow -Enabled True -Profile Any -Program $AppExe -Protocol UDP -LocalPort $mdnsPort -RemoteAddress Any -InterfaceType Any -ErrorAction Stop | Out-Null
} else {
    Write-Host 'The executable was not found. Port-based rules were still added.'
}

Write-Host
Write-Host 'Windows Firewall rules were added successfully.'
Write-Host "Sync TCP port: $syncPort"
Write-Host "mDNS UDP port: $mdnsPort"
