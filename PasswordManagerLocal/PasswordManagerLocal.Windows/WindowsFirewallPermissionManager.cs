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
                Details = string.IsNullOrWhiteSpace(applyResult.Error) ? applyResult.Output : applyResult.Error
            };
        }

        return new FirewallPermissionCheckResult
        {
            IsSupported = true,
            IsConfigured = true,
            CanRequestPermission = true
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
        $addressFilter = $rule | Get-NetFirewallAddressFilter

        $protocolValue = [string]$portFilter.Protocol
        $localPorts = @($portFilter.LocalPort | ForEach-Object { [string]$_ })
        $remoteAddresses = @($addressFilter.RemoteAddress | ForEach-Object { [string]$_ })
        $profileText = [string]$rule.Profile

        $protocolOk =
            $protocolValue -ieq $Protocol -or
            ($Protocol -ieq 'TCP' -and $protocolValue -eq '6') -or
            ($Protocol -ieq 'UDP' -and $protocolValue -eq '17')

        $portOk = $localPorts -contains $Port -or $localPorts -contains 'Any'
        $remoteOk = $remoteAddresses -contains 'LocalSubnet' -or $remoteAddresses -contains 'Any' -or $remoteAddresses -contains '*'
        $profileOk =
            $profileText -ieq 'Any' -or
            $profileText.Contains('Private') -or
            $profileText.Contains('Domain') -or
            $profileText.Contains('Public')

        if ($protocolOk -and $portOk -and $remoteOk -and $profileOk) {
            return $true
        }
    }

    return $false
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

        var deleteLines = string.Join(Environment.NewLine, allRuleNames.Select(name => $"& $netsh advfirewall firewall delete rule name='{EscapePowerShellSingleQuotedString(name)}' | Out-Null"));

        return $$"""
param([string]$AppExe)
$ErrorActionPreference = 'Stop'
$netsh = Join-Path $env:WINDIR 'System32\netsh.exe'

function Invoke-PmlNetsh {
    param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)

    & $netsh @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "netsh failed: $($Arguments -join ' ')"
    }
}

function Invoke-PmlNetshOptional {
    param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)

    & $netsh @Arguments | Out-Null
}

{{deleteLines}}

Invoke-PmlNetsh advfirewall firewall add rule name='{{TcpPortRuleName}}' dir=in action=allow enable=yes profile=any protocol=TCP localport={{SyncConstants.SyncPort}} remoteip=localsubnet
Invoke-PmlNetsh advfirewall firewall add rule name='{{MdnsPortRuleName}}' dir=in action=allow enable=yes profile=any protocol=UDP localport=5353 remoteip=localsubnet

if (-not [string]::IsNullOrWhiteSpace($AppExe) -and (Test-Path -LiteralPath $AppExe)) {
    Invoke-PmlNetshOptional advfirewall firewall add rule name='{{TcpAppRuleName}}' dir=in action=allow program="$AppExe" enable=yes profile=any protocol=TCP localport={{SyncConstants.SyncPort}} remoteip=localsubnet
    Invoke-PmlNetshOptional advfirewall firewall add rule name='{{MdnsAppRuleName}}' dir=in action=allow program="$AppExe" enable=yes profile=any protocol=UDP localport=5353 remoteip=localsubnet
}

exit 0
""";
    }



    private static async Task<(int ExitCode, string Output, string Error)> RunPowerShellScriptAsync(string script, string appExePath, bool elevated, CancellationToken ct)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"pml-firewall-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(scriptPath, script, ct);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File {QuoteArgument(scriptPath)} -AppExe {QuoteArgument(appExePath)}",
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
        }
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
