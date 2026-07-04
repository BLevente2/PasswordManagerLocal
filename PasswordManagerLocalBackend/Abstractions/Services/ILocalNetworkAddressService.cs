using System.Net;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface ILocalNetworkAddressService
{
    IReadOnlyList<string> GetPreferredLocalHosts();
    IReadOnlyList<IPAddress> GetMulticastInterfaceAddresses();
    IPAddress? GetRoutedLocalAddress(IPAddress remoteAddress);
    int GetRemoteEndpointPriority(string host);
}
