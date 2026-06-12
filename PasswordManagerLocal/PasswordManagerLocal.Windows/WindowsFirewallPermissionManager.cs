using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PasswordManagerLocal.Services;
using PasswordManagerLocalBackend.Constants;


namespace PasswordManagerLocal.Windows;

internal sealed class WindowsFirewallPermissionManager : IFirewallPermissionManager
{
    private const string TcpAppRuleName = "PasswordManagerLocal Sync TCP App";
    private const string TcpPortRuleName = "PasswordManagerLocal Sync TCP Port";
    private const string MdnsAppRuleName = "PasswordManagerLocal mDNS UDP App";
    private const string MdnsPortRuleName = "PasswordManagerLocal mDNS UDP Port";
    private static readonly string[] LegacyRuleNames =
    [
        "PasswordManagerLocal Sync TCP",
        "PasswordManagerLocal mDNS UDP"
    ];

    public async Task<FirewallPermissionCheckResult> CheckAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return FirewallPermissionCheckResult.Unsupported();

        var appExePath = GetApplicationPath();
        if (string.IsNullOrWhiteSpace(appExePath) || !File.Exists(appExePath))
        {
            return new FirewallPermissionCheckResult
            {
                IsSupported = true,
                IsConfigured = false,
                CanRequestPermission = true,
                Details = "The application executable path could not be determined, but the port based firewall rules can still be configured."
            };
        }

        var script = CreateCheckScript();
        var result = await RunPowerShellScriptAsync(script, appExePath, elevated: false, ct);

        if (result.ExitCode == 0)
        {
            return new FirewallPermissionCheckResult
            {
                IsSupported = true,
                IsConfigured = true,
                CanRequestPermission = true
            };
        }

        return new FirewallPermissionCheckResult
        {
            IsSupported = true,
            IsConfigured = false,
            CanRequestPermission = true,
            Details = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error
        };
    }



    public async Task<FirewallPermissionCheckResult> RequestPermissionAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return FirewallPermissionCheckResult.Unsupported();

        var appExePath = GetApplicationPath();
        if (string.IsNullOrWhiteSpace(appExePath) || !File.Exists(appExePath))
            appExePath = string.Empty;

        var script = CreateApplyScript();
        var applyResult = await RunPowerShellScriptAsync(script, appExePath, elevated: true, ct);
        if (applyResult.ExitCode != 0)
        {
            return new FirewallPermissionCheckResult
            {
                IsSupported = true,
                IsConfigured = false,
                CanRequestPermission = true,
                Details = GetBestProcessDetails(applyResult)
            };
        }

        return new FirewallPermissionCheckResult
        {
            IsSupported = true,
            IsConfigured = true,
            CanRequestPermission = true,
            Details = GetBestProcessDetails(applyResult)
        };
    }



    private static string? GetApplicationPath()
    {
        try
        {
            return Environment.ProcessPath;
        }
        catch
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
    }



    private static string CreateCheckScript() =>
        $$"""
param([string]$AppExe)
$ErrorActionPreference = 'Stop'
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

function Get-PmlAppRules {
    param(
        [string]$AppExe,
        [string]$Action
    )

    if ([string]::IsNullOrWhiteSpace($AppExe) -or -not (Test-Path -LiteralPath $AppExe)) {
        return @()
    }

    $matched = @()
    $rules = @(Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object {
        ([string]$_.Enabled) -ieq 'True' -and
        ([string]$_.Direction) -ieq 'Inbound' -and
        ([string]$_.Action) -ieq $Action
    })

    foreach ($rule in $rules) {
        $filters = @($rule | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue)
        foreach ($filter in $filters) {
            if (Test-PmlProgramMatch -RuleProgram ([string]$filter.Program) -AppExe $AppExe) {
                $matched += $rule
                break
            }
        }
    }

    return $matched
}

function Test-PmlFirewallPortRule {
    param(
        [string]$DisplayName,
        [string]$Protocol,
        [string]$Port
    )

    $rules = @(Get-NetFirewallRule -DisplayName $DisplayName -ErrorAction SilentlyContinue | Where-Object {
        ([string]$_.Enabled) -ieq 'True' -and
        ([string]$_.Direction) -ieq 'Inbound' -and
        ([string]$_.Action) -ieq 'Allow'
    })

    foreach ($rule in $rules) {
        $portFilter = $rule | Get-NetFirewallPortFilter
        $protocolValue = [string]$portFilter.Protocol
        $localPorts = @($portFilter.LocalPort | ForEach-Object { [string]$_ })
        $profileText = [string]$rule.Profile

        $protocolOk =
            $protocolValue -ieq $Protocol -or
            ($Protocol -ieq 'TCP' -and $protocolValue -eq '6') -or
            ($Protocol -ieq 'UDP' -and $protocolValue -eq '17')

        $portOk = $localPorts -contains $Port -or $localPorts -contains 'Any'
        $profileOk =
            $profileText -ieq 'Any' -or
            $profileText.Contains('Private') -or
            $profileText.Contains('Domain') -or
            $profileText.Contains('Public')

        if ($protocolOk -and $portOk -and $profileOk) {
            return $true
        }
    }

    return $false
}

$appBlockRules = @(Get-PmlAppRules -AppExe $AppExe -Action 'Block')
if ($appBlockRules.Count -gt 0) {
    Write-Error "A Windows Firewall inbound block rule is active for this executable: $($appBlockRules[0].DisplayName). Click Allow again so PasswordManagerLocal can replace the blocked app rule with an allow rule."
    exit 3
}
$tcpOk = Test-PmlFirewallPortRule -DisplayName '{{TcpPortRuleName}}' -Protocol 'TCP' -Port '{{SyncConstants.SyncPort}}'
$udpOk = Test-PmlFirewallPortRule -DisplayName '{{MdnsPortRuleName}}' -Protocol 'UDP' -Port '5353'

if ($tcpOk -and $udpOk) {
    exit 0
}

exit 2
""";



    private static string CreateApplyScript()
    {
        var allRuleNames = new[]
        {
            TcpAppRuleName,
            TcpPortRuleName,
            MdnsAppRuleName,
            MdnsPortRuleName
        }.Concat(LegacyRuleNames);

        var deleteLines = string.Join(Environment.NewLine, allRuleNames.Select(name => $"Remove-PmlFirewallRuleByDisplayName -DisplayName '{EscapePowerShellSingleQuotedString(name)}'"));

        return $$"""
param(
    [string]$AppExe,
    [string]$StatusFile
)

$ErrorActionPreference = 'Stop'

try {
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
                $rule | Remove-NetFirewallRule -ErrorAction SilentlyContinue
                break
            }
        }
    }
}

{{deleteLines}}
Remove-PmlConflictingAppBlockRules -AppExe $AppExe
New-NetFirewallRule -DisplayName '{{TcpPortRuleName}}' -Direction Inbound -Action Allow -Enabled True -Profile Any -Protocol TCP -LocalPort {{SyncConstants.SyncPort}} -RemoteAddress Any -InterfaceType Any -ErrorAction Stop | Out-Null
New-NetFirewallRule -DisplayName '{{MdnsPortRuleName}}' -Direction Inbound -Action Allow -Enabled True -Profile Any -Protocol UDP -LocalPort 5353 -RemoteAddress Any -InterfaceType Any -ErrorAction Stop | Out-Null

if (-not [string]::IsNullOrWhiteSpace($AppExe) -and (Test-Path -LiteralPath $AppExe)) {
    New-NetFirewallRule -DisplayName '{{TcpAppRuleName}}' -Direction Inbound -Action Allow -Enabled True -Profile Any -Program $AppExe -Protocol TCP -LocalPort {{SyncConstants.SyncPort}} -RemoteAddress Any -InterfaceType Any -ErrorAction Stop | Out-Null
    New-NetFirewallRule -DisplayName '{{MdnsAppRuleName}}' -Direction Inbound -Action Allow -Enabled True -Profile Any -Program $AppExe -Protocol UDP -LocalPort 5353 -RemoteAddress Any -InterfaceType Any -ErrorAction Stop | Out-Null
}

if (-not [string]::IsNullOrWhiteSpace($StatusFile)) {
    Set-Content -LiteralPath $StatusFile -Value 'OK: Windows Firewall rules were added successfully.' -Encoding UTF8
}

exit 0
} catch {
    $message = $_.Exception.Message
    if (-not [string]::IsNullOrWhiteSpace($StatusFile)) {
        Set-Content -LiteralPath $StatusFile -Value ("ERROR: " + $message) -Encoding UTF8
    }

    [Console]::Error.WriteLine($message)
    exit 1
}
""";
    }



    private static async Task<(int ExitCode, string Output, string Error)> RunPowerShellScriptAsync(string script, string appExePath, bool elevated, CancellationToken ct)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"pml-firewall-{Guid.NewGuid():N}.ps1");
        var statusPath = elevated ? Path.Combine(Path.GetTempPath(), $"pml-firewall-status-{Guid.NewGuid():N}.txt") : null;
        await File.WriteAllTextAsync(scriptPath, script, ct);

        try
        {
            var arguments = $"-NoProfile -ExecutionPolicy Bypass -File {QuoteArgument(scriptPath)} -AppExe {QuoteArgument(appExePath)}";
            if (!string.IsNullOrWhiteSpace(statusPath))
                arguments += $" -StatusFile {QuoteArgument(statusPath)}";

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = arguments,
                UseShellExecute = elevated,
                CreateNoWindow = !elevated
            };

            if (elevated)
            {
                startInfo.Verb = "runas";
            }
            else
            {
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
            }

            using var process = Process.Start(startInfo);
            if (process is null)
                return (-1, string.Empty, "Could not start PowerShell.");

            string output = string.Empty;
            string error = string.Empty;

            if (!elevated)
            {
                var outputTask = process.StandardOutput.ReadToEndAsync(ct);
                var errorTask = process.StandardError.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);
                output = await outputTask;
                error = await errorTask;
            }
            else
            {
                await process.WaitForExitAsync(ct);
                if (!string.IsNullOrWhiteSpace(statusPath) && File.Exists(statusPath))
                    output = await File.ReadAllTextAsync(statusPath, ct);
            }

            return (process.ExitCode, output.Trim(), error.Trim());
        }
        catch (System.ComponentModel.Win32Exception ex) when ((uint)ex.NativeErrorCode == 1223)
        {
            return (-1, string.Empty, "The permission request was canceled by the user.");
        }
        finally
        {
            TryDeleteFile(scriptPath);
            if (!string.IsNullOrWhiteSpace(statusPath))
                TryDeleteFile(statusPath);
        }
    }



    private static string GetBestProcessDetails((int ExitCode, string Output, string Error) result)
    {
        if (!string.IsNullOrWhiteSpace(result.Error))
            return result.Error;

        if (!string.IsNullOrWhiteSpace(result.Output))
            return result.Output;

        return $"PowerShell exited with code {result.ExitCode}.";
    }



    private static string QuoteArgument(string value) =>
        $"\"{value.Replace("\"", "\\\"")}\"";



    private static string EscapePowerShellSingleQuotedString(string value) =>
        value.Replace("'", "''");



    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
