using Makaretu.Dns;
using Microsoft.Extensions.Hosting;
using PasswordManagerLocalBackend.Abstractions.Services;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using static PasswordManagerLocalBackend.Constants.SyncConstants;

namespace PasswordManagerLocalBackend.Services.Hosted;

public sealed class MdnsPublisherHostedService : ISyncControlledHostedService
{
    private readonly IDeviceIdentityService _identity;
    private ServiceDiscovery? _serviceDiscovery;
    private ServiceProfile? _profile;

    public MdnsPublisherHostedService(IDeviceIdentityService identity)
    {
        _identity = identity;
    }




    public int StartOrder => 30;




    public Task StartAsync(CancellationToken ct = default)
    {
        if (!_identity.IsSyncOn)
            return Task.CompletedTask;

        if (_serviceDiscovery is not null && _profile is not null)
            return Task.CompletedTask;

        _serviceDiscovery = new ServiceDiscovery();
        _profile = new ServiceProfile(BuildInstanceName(), MdnsServiceType, (ushort)SyncPort);

        _profile.AddProperty("deviceid", _identity.DeviceIdHex);
        _profile.AddProperty("deviceguid", _identity.LocalDeviceId.ToString("N"));
        _profile.AddProperty("signpub", Convert.ToHexString(_identity.SignPublicKey));
        _profile.AddProperty("agreepub", Convert.ToHexString(_identity.AgreementPublicKey));
        _profile.AddProperty("tlsfp", _identity.FingerprintHex);

        var hosts = BuildHostsProperty(GetLocalSyncHosts());
        if (!string.IsNullOrWhiteSpace(hosts))
            _profile.AddProperty("hosts", hosts);

        _serviceDiscovery.Advertise(_profile);
        _serviceDiscovery.Announce(_profile);

        return Task.CompletedTask;
    }


    public Task StopAsync(CancellationToken ct = default)
    {
        if (_serviceDiscovery is not null)
        {
            if (_profile is not null)
                _serviceDiscovery.Unadvertise(_profile);

            _serviceDiscovery.Dispose();
            _serviceDiscovery = null;
            _profile = null;
        }

        return Task.CompletedTask;
    }


    private string BuildInstanceName()
    {
        var id = _identity.DeviceIdHex;
        return $"pml-{id[..Math.Min(24, id.Length)]}";
    }


    private static IReadOnlyList<string> GetLocalSyncHosts()
    {
        var candidates = new List<LocalSyncHostCandidate>();

        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                var properties = networkInterface.GetIPProperties();
                var hasGateway = properties.GatewayAddresses.Any(gateway => IsUsableUnicastAddress(gateway.Address));
                var isVirtualAdapter = IsVirtualOrNonLanAdapter(networkInterface);
                var typePriority = GetNetworkInterfaceTypePriority(networkInterface.NetworkInterfaceType);

                foreach (var addressInfo in properties.UnicastAddresses)
                {
                    var address = addressInfo.Address;
                    if (!IsUsableUnicastAddress(address))
                        continue;

                    if (IsWindowsHostOnlyGatewayAddress(address, isVirtualAdapter))
                        continue;

                    var priority = 0;

                    if (!isVirtualAdapter)
                        priority += 10000;
                    else
                        priority -= 10000;

                    if (hasGateway)
                        priority += 4000;

                    priority += typePriority;

                    if (address.AddressFamily == AddressFamily.InterNetwork)
                        priority += 2000;
                    else
                        priority -= 500;

                    priority += GetPrivateAddressPriority(address);

                    if (addressInfo.PrefixOrigin is PrefixOrigin.Dhcp or PrefixOrigin.Manual)
                        priority += 250;

                    candidates.Add(new LocalSyncHostCandidate
                    {
                        Address = address,
                        Priority = priority,
                        IsVirtualAdapter = isVirtualAdapter
                    });
                }
            }
        }
        catch
        {
        }

        var hasPhysicalIpv4Candidate = candidates.Any(candidate =>
            !candidate.IsVirtualAdapter &&
            candidate.Address.AddressFamily == AddressFamily.InterNetwork &&
            IsPrivateIpv4(candidate.Address));

        if (hasPhysicalIpv4Candidate)
        {
            candidates = candidates
                .Where(candidate => !candidate.IsVirtualAdapter && candidate.Address.AddressFamily == AddressFamily.InterNetwork)
                .ToList();
        }
        else if (candidates.Any(candidate => !candidate.IsVirtualAdapter))
        {
            candidates = candidates
                .Where(candidate => !candidate.IsVirtualAdapter)
                .ToList();
        }

        if (candidates.Count == 0)
        {
            try
            {
                foreach (var address in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (!IsUsableUnicastAddress(address) || IsWindowsHostOnlyGatewayAddress(address, true))
                        continue;

                    candidates.Add(new LocalSyncHostCandidate
                    {
                        Address = address,
                        Priority = address.AddressFamily == AddressFamily.InterNetwork ? 100 : 50,
                        IsVirtualAdapter = false
                    });
                }
            }
            catch
            {
            }
        }

        return candidates
            .GroupBy(candidate => candidate.Address.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(candidate => candidate.Priority).First())
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Address.ToString(), StringComparer.Ordinal)
            .Select(candidate => candidate.Address.ToString())
            .ToList();
    }


    private static string BuildHostsProperty(IReadOnlyList<string> hosts)
    {
        var selected = new List<string>();
        var totalLength = 0;

        foreach (var host in hosts.Where(host => IPAddress.TryParse(host, out _)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var extraLength = host.Length + (selected.Count == 0 ? 0 : 1);
            if (totalLength + extraLength > 220)
                break;

            selected.Add(host);
            totalLength += extraLength;

            if (selected.Count >= 16)
                break;
        }

        return string.Join(',', selected);
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


    private static int GetNetworkInterfaceTypePriority(NetworkInterfaceType interfaceType) =>
        interfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => 3000,
            NetworkInterfaceType.Ethernet => 2500,
            NetworkInterfaceType.GigabitEthernet => 2500,
            NetworkInterfaceType.FastEthernetFx => 2500,
            NetworkInterfaceType.FastEthernetT => 2500,
            NetworkInterfaceType.Ppp => -1000,
            _ => 0
        };


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


    private static bool IsWindowsHostOnlyGatewayAddress(IPAddress address, bool isVirtualAdapter)
    {
        if (!isVirtualAdapter || address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
            return false;

        if (bytes[3] != 1)
            return false;

        return true;
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


    private sealed class LocalSyncHostCandidate
    {
        public IPAddress Address { get; set; } = IPAddress.None;
        public int Priority { get; set; }
        public bool IsVirtualAdapter { get; set; }
    }
}
