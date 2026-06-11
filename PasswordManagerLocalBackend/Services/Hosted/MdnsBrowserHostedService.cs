using Makaretu.Dns;
using Microsoft.Extensions.Hosting;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Sync;
using System.Collections.Concurrent;
using static PasswordManagerLocalBackend.Constants.SyncConstants;

namespace PasswordManagerLocalBackend.Services.Hosted;

public sealed class MdnsBrowserHostedService : ISyncControlledHostedService
{
    private readonly IDeviceIdentityService _identity;
    private readonly ISyncDeviceIdentityService _syncDeviceIdentities;
    private readonly IDeviceSyncTaskService _deviceSyncTasks;
    private readonly ConcurrentDictionary<string, DateTime> _lastSeen = new(StringComparer.OrdinalIgnoreCase);
    private ServiceDiscovery? _serviceDiscovery;

    public MdnsBrowserHostedService(
        IDeviceIdentityService identity,
        ISyncDeviceIdentityService syncDeviceIdentities,
        IDeviceSyncTaskService deviceSyncTasks)
    {
        _identity = identity;
        _syncDeviceIdentities = syncDeviceIdentities;
        _deviceSyncTasks = deviceSyncTasks;
    }




    public int StartOrder => 40;




    public Task StartAsync(CancellationToken ct = default)
    {
        if (!_identity.IsSyncOn)
            return Task.CompletedTask;

        if (_serviceDiscovery is not null)
            return Task.CompletedTask;

        _serviceDiscovery = new ServiceDiscovery();
        _serviceDiscovery.ServiceInstanceDiscovered += OnServiceInstanceDiscovered;
        _serviceDiscovery.QueryServiceInstances(MdnsServiceType);

        return Task.CompletedTask;
    }


    public Task StopAsync(CancellationToken ct = default)
    {
        if (_serviceDiscovery is not null)
        {
            _serviceDiscovery.ServiceInstanceDiscovered -= OnServiceInstanceDiscovered;
            _serviceDiscovery.Dispose();
            _serviceDiscovery = null;
        }

        return Task.CompletedTask;
    }


    private async void OnServiceInstanceDiscovered(object? sender, ServiceInstanceDiscoveryEventArgs e)
    {
        try
        {
            if (!_identity.IsSyncOn)
                return;

            var mdns = _serviceDiscovery?.Mdns;
            if (mdns is null)
                return;

            var instance = e.ServiceInstanceName;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(MdnsResolveTimeoutSeconds));

            var txt = await ResolveTxtAsync(mdns, instance, cts.Token);
            if (txt is null)
                return;

            if (!txt.TryGetValue("tlsfp", out var tlsFingerprint))
                return;

            if (string.Equals(tlsFingerprint, _identity.FingerprintHex, StringComparison.OrdinalIgnoreCase))
                return;

            if (txt.TryGetValue("signpub", out var signPublicKeyHex))
            {
                var signPublicKey = Convert.FromHexString(signPublicKeyHex);
                if (_identity.SignPublicKey.SequenceEqual(signPublicKey))
                    return;
            }

            if (IsThrottled(tlsFingerprint))
                return;

            if (!_syncDeviceIdentities.TryGetByFingerprint(tlsFingerprint, out var device) || device is null)
                return;

            if (!_identity.IsSyncOn || !device.IsTrusted || device.IsBlocked || device.Id == _identity.LocalDeviceId)
                return;

            var endpoint = await ResolveEndpointAsync(mdns, instance, tlsFingerprint, cts.Token);
            if (endpoint is null)
                return;

            _deviceSyncTasks.TryStart(endpoint, device);
        }
        catch
        {
        }
    }


    private async Task<IReadOnlyDictionary<string, string>?> ResolveTxtAsync(MulticastService mdns, DomainName instance, CancellationToken ct)
    {
        var query = new Message();
        query.Questions.Add(new Question { Name = instance, Type = DnsType.TXT });

        var response = await mdns.ResolveAsync(query, ct);
        var record = response.Answers.Concat(response.AdditionalRecords).OfType<TXTRecord>().FirstOrDefault();
        if (record is null)
            return null;

        return record.Strings
            .Where(s => s.Contains('='))
            .Select(s =>
            {
                var separatorIndex = s.IndexOf('=');
                return new KeyValuePair<string, string>(s[..separatorIndex], s[(separatorIndex + 1)..]);
            })
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Value, StringComparer.OrdinalIgnoreCase);
    }


    private async Task<DiscoveredDeviceEndpoint?> ResolveEndpointAsync(MulticastService mdns, DomainName instance, string tlsFingerprint, CancellationToken ct)
    {
        var srvQuery = new Message();
        srvQuery.Questions.Add(new Question { Name = instance, Type = DnsType.SRV });

        var srvResponse = await mdns.ResolveAsync(srvQuery, ct);
        var srv = srvResponse.Answers.Concat(srvResponse.AdditionalRecords).OfType<SRVRecord>().FirstOrDefault();
        if (srv is null)
            return null;

        var host = await ResolveHostAsync(mdns, srv.Target, ct);
        if (host is null)
            return null;

        return new DiscoveredDeviceEndpoint
        {
            Host = host,
            Port = srv.Port,
            TlsCertFingerprint = tlsFingerprint
        };
    }


    private static async Task<string?> ResolveHostAsync(MulticastService mdns, DomainName target, CancellationToken ct)
    {
        var aQuery = new Message();
        aQuery.Questions.Add(new Question { Name = target, Type = DnsType.A });

        var aResponse = await mdns.ResolveAsync(aQuery, ct);
        var a = aResponse.Answers.Concat(aResponse.AdditionalRecords).OfType<ARecord>().FirstOrDefault();
        if (a is not null)
            return a.Address.ToString();

        var aaaaQuery = new Message();
        aaaaQuery.Questions.Add(new Question { Name = target, Type = DnsType.AAAA });

        var aaaaResponse = await mdns.ResolveAsync(aaaaQuery, ct);
        var aaaa = aaaaResponse.Answers.Concat(aaaaResponse.AdditionalRecords).OfType<AAAARecord>().FirstOrDefault();
        return aaaa?.Address.ToString();
    }


    private bool IsThrottled(string tlsFingerprint)
    {
        var now = DateTime.UtcNow;

        if (_lastSeen.TryGetValue(tlsFingerprint, out var last) && (now - last).TotalSeconds < MdnsThrottleSeconds)
            return true;

        _lastSeen[tlsFingerprint] = now;
        return false;
    }
}
