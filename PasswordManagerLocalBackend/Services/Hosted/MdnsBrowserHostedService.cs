using Makaretu.Dns;
using Microsoft.Extensions.Hosting;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Sync;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using static PasswordManagerLocalBackend.Constants.SyncConstants;

namespace PasswordManagerLocalBackend.Services.Hosted;

public sealed class MdnsBrowserHostedService : ISyncControlledHostedService
{
    private readonly IDeviceIdentityService _identity;
    private readonly ISyncDeviceIdentityService _syncDeviceIdentities;
    private readonly IDiscoveredDeviceEndpointCache _endpointCache;
    private readonly IDeviceSyncTaskService _deviceSyncTasks;
    private readonly ConcurrentDictionary<string, DateTime> _lastSeen = new(StringComparer.OrdinalIgnoreCase);
    private ServiceDiscovery? _serviceDiscovery;
    private CancellationTokenSource? _refreshLoopCancellation;
    private Task? _refreshLoopTask;

    public MdnsBrowserHostedService(
        IDeviceIdentityService identity,
        ISyncDeviceIdentityService syncDeviceIdentities,
        IDiscoveredDeviceEndpointCache endpointCache,
        IDeviceSyncTaskService deviceSyncTasks)
    {
        _identity = identity;
        _syncDeviceIdentities = syncDeviceIdentities;
        _endpointCache = endpointCache;
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

        QueryServiceInstances();
        StartRefreshLoop();

        return Task.CompletedTask;
    }


    public async Task StopAsync(CancellationToken ct = default)
    {
        var refreshLoopTask = _refreshLoopTask;
        var refreshLoopCancellation = _refreshLoopCancellation;
        _refreshLoopTask = null;
        _refreshLoopCancellation = null;

        if (refreshLoopCancellation is not null)
        {
            try
            {
                refreshLoopCancellation.Cancel();
            }
            catch
            {
            }

            if (refreshLoopTask is not null)
            {
                try
                {
                    await Task.WhenAny(refreshLoopTask, Task.Delay(TimeSpan.FromSeconds(2), ct));
                }
                catch
                {
                }
            }

            refreshLoopCancellation.Dispose();
        }

        if (_serviceDiscovery is not null)
        {
            _serviceDiscovery.ServiceInstanceDiscovered -= OnServiceInstanceDiscovered;
            _serviceDiscovery.Dispose();
            _serviceDiscovery = null;
        }

        _endpointCache.Clear();
        _lastSeen.Clear();
    }


    private void StartRefreshLoop()
    {
        _refreshLoopCancellation?.Cancel();
        _refreshLoopCancellation?.Dispose();

        _refreshLoopCancellation = new CancellationTokenSource();
        var token = _refreshLoopCancellation.Token;
        _refreshLoopTask = Task.Run(() => RefreshLoopAsync(token), CancellationToken.None);
    }


    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(MdnsRefreshIntervalSeconds));
            while (await timer.WaitForNextTickAsync(ct))
                QueryServiceInstances();
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }


    private void QueryServiceInstances()
    {
        try
        {
            if (_identity.IsSyncOn)
                _serviceDiscovery?.QueryServiceInstances(MdnsServiceType);
        }
        catch
        {
        }
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

            if (string.Equals(NormalizeFingerprint(tlsFingerprint), NormalizeFingerprint(_identity.FingerprintHex), StringComparison.OrdinalIgnoreCase))
                return;

            if (txt.TryGetValue("signpub", out var signPublicKeyHex))
            {
                var signPublicKey = Convert.FromHexString(signPublicKeyHex);
                if (_identity.SignPublicKey.SequenceEqual(signPublicKey))
                    return;
            }

            if (txt.TryGetValue("deviceguid", out var deviceGuid) &&
                Guid.TryParseExact(deviceGuid, "N", out var advertisedDeviceId) &&
                advertisedDeviceId == _identity.LocalDeviceId)
                return;

            var endpoints = await ResolveEndpointsAsync(mdns, instance, txt, tlsFingerprint, cts.Token);
            if (endpoints.Count == 0)
                return;

            var endpoint = endpoints[0];
            _endpointCache.AddOrUpdate(endpoint);

            if (IsThrottled(tlsFingerprint))
                return;

            if (!_syncDeviceIdentities.TryGetByFingerprint(tlsFingerprint, out var device) || device is null)
                return;

            if (!_identity.IsSyncOn || !device.IsTrusted || device.IsBlocked || device.Id == _identity.LocalDeviceId)
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


    private async Task<IReadOnlyList<DiscoveredDeviceEndpoint>> ResolveEndpointsAsync(
        MulticastService mdns,
        DomainName instance,
        IReadOnlyDictionary<string, string> txt,
        string tlsFingerprint,
        CancellationToken ct)
    {
        var srvQuery = new Message();
        srvQuery.Questions.Add(new Question { Name = instance, Type = DnsType.SRV });

        var srvResponse = await mdns.ResolveAsync(srvQuery, ct);
        var srv = srvResponse.Answers.Concat(srvResponse.AdditionalRecords).OfType<SRVRecord>().FirstOrDefault();
        if (srv is null)
            return [];

        var hosts = new List<string>();
        if (txt.TryGetValue("hosts", out var advertisedHosts))
            hosts.AddRange(ParseAdvertisedHosts(advertisedHosts));

        hosts.AddRange(await ResolveHostsAsync(mdns, srv.Target, ct));

        return hosts
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Select(host => host.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(host => new DiscoveredDeviceEndpoint
            {
                Host = host,
                Port = srv.Port,
                TlsCertFingerprint = tlsFingerprint
            })
            .Select(endpoint => new { Endpoint = endpoint, Priority = GetEndpointPriorityForThisDevice(endpoint.Host) })
            .Where(candidate => candidate.Priority > int.MinValue)
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Endpoint.Host, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Endpoint)
            .ToList();
    }


    private static IReadOnlyList<string> ParseAdvertisedHosts(string advertisedHosts) =>
        advertisedHosts
            .Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(host => IPAddress.TryParse(host, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();


    private static async Task<IReadOnlyList<string>> ResolveHostsAsync(MulticastService mdns, DomainName target, CancellationToken ct)
    {
        var hosts = new List<string>();

        var aQuery = new Message();
        aQuery.Questions.Add(new Question { Name = target, Type = DnsType.A });

        var aResponse = await mdns.ResolveAsync(aQuery, ct);
        hosts.AddRange(aResponse.Answers.Concat(aResponse.AdditionalRecords).OfType<ARecord>().Select(record => record.Address.ToString()));

        var aaaaQuery = new Message();
        aaaaQuery.Questions.Add(new Question { Name = target, Type = DnsType.AAAA });

        var aaaaResponse = await mdns.ResolveAsync(aaaaQuery, ct);
        hosts.AddRange(aaaaResponse.Answers.Concat(aaaaResponse.AdditionalRecords).OfType<AAAARecord>().Select(record => record.Address.ToString()));

        return hosts
            .Where(host => IPAddress.TryParse(host, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }


    private static int GetEndpointPriorityForThisDevice(string host)
    {
        if (!IPAddress.TryParse(host, out var remoteAddress))
            return 0;

        if (!IsUsableUnicastAddress(remoteAddress))
            return int.MinValue;

        var localNetworks = GetLocalIpv4Networks();
        if (remoteAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            if (localNetworks.Any(network => network.Address.Equals(remoteAddress)))
                return int.MinValue;

            var samePhysicalSubnet = localNetworks.Any(network => !network.IsVirtualAdapter && IsInSameIpv4Subnet(remoteAddress, network.Address, network.Mask));
            if (samePhysicalSubnet)
                return 5000 + GetPrivateAddressPriority(remoteAddress);

            var sameVirtualSubnet = localNetworks.Any(network => network.IsVirtualAdapter && IsInSameIpv4Subnet(remoteAddress, network.Address, network.Mask));
            if (sameVirtualSubnet)
                return 2500 + GetPrivateAddressPriority(remoteAddress);

            if (IsPrivateIpv4(remoteAddress))
                return 100 + GetPrivateAddressPriority(remoteAddress);
        }

        return remoteAddress.AddressFamily == AddressFamily.InterNetwork ? 0 : -100;
    }


    private static List<LocalIpv4Network> GetLocalIpv4Networks()
    {
        var networks = new List<LocalIpv4Network>();

        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                var isVirtualAdapter = IsVirtualOrNonLanAdapter(networkInterface);
                foreach (var addressInfo in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (addressInfo.Address.AddressFamily != AddressFamily.InterNetwork ||
                        addressInfo.IPv4Mask is null ||
                        !IsUsableUnicastAddress(addressInfo.Address))
                        continue;

                    networks.Add(new LocalIpv4Network
                    {
                        Address = addressInfo.Address,
                        Mask = addressInfo.IPv4Mask,
                        IsVirtualAdapter = isVirtualAdapter
                    });
                }
            }
        }
        catch
        {
        }

        return networks;
    }


    private static bool IsInSameIpv4Subnet(IPAddress remoteAddress, IPAddress localAddress, IPAddress mask)
    {
        if (remoteAddress.AddressFamily != AddressFamily.InterNetwork ||
            localAddress.AddressFamily != AddressFamily.InterNetwork ||
            mask.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var remoteBytes = remoteAddress.GetAddressBytes();
        var localBytes = localAddress.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();

        if (remoteBytes.Length != 4 || localBytes.Length != 4 || maskBytes.Length != 4)
            return false;

        for (var i = 0; i < 4; i++)
        {
            if ((remoteBytes[i] & maskBytes[i]) != (localBytes[i] & maskBytes[i]))
                return false;
        }

        return true;
    }


    private static bool IsUsableUnicastAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast) || address.Equals(IPAddress.IPv6Any))
            return false;

        if (address.AddressFamily == AddressFamily.InterNetwork)
            return !IsApipaIpv4(address);

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return false;

        return !address.IsIPv6LinkLocal && !address.IsIPv6Multicast && !address.IsIPv6SiteLocal;
    }


    private static bool IsApipaIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }


    private static bool IsVirtualOrNonLanAdapter(NetworkInterface networkInterface)
    {
        var text = $"{networkInterface.Name} {networkInterface.Description}".ToLowerInvariant();

        return text.Contains("virtual") ||
               text.Contains("vethernet") ||
               text.Contains("hyper-v") ||
               text.Contains("default switch") ||
               text.Contains("wsl") ||
               text.Contains("docker") ||
               text.Contains("vmware") ||
               text.Contains("virtualbox") ||
               text.Contains("vmnet") ||
               text.Contains("host-only") ||
               text.Contains("loopback") ||
               text.Contains("npcap") ||
               text.Contains("bluetooth") ||
               text.Contains("vpn") ||
               text.Contains("tap") ||
               text.Contains("tun") ||
               text.Contains("tailscale") ||
               text.Contains("zerotier") ||
               text.Contains("wireguard") ||
               text.Contains("pseudo");
    }


    private static int GetPrivateAddressPriority(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            if (bytes.Length != 4)
                return 0;

            if (bytes[0] == 192 && bytes[1] == 168)
                return 500;

            if (bytes[0] == 10)
                return 400;

            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return 250;

            return -500;
        }

        return IsPrivateIpv6(address) ? 100 : -500;
    }


    private static bool IsPrivateIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 &&
               (bytes[0] == 10 ||
                (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168));
    }


    private static bool IsPrivateIpv6(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return false;

        var bytes = address.GetAddressBytes();
        return bytes.Length == 16 && (bytes[0] & 0xFE) == 0xFC;
    }


    private bool IsThrottled(string tlsFingerprint)
    {
        var now = DateTime.UtcNow;

        if (_lastSeen.TryGetValue(tlsFingerprint, out var last) && (now - last).TotalSeconds < MdnsThrottleSeconds)
            return true;

        _lastSeen[tlsFingerprint] = now;
        return false;
    }


    private static string NormalizeFingerprint(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return string.Empty;

        return fingerprint.Replace(":", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
    }


    private sealed class LocalIpv4Network
    {
        public IPAddress Address { get; set; } = IPAddress.None;
        public IPAddress Mask { get; set; } = IPAddress.None;
        public bool IsVirtualAdapter { get; set; }
    }
}
