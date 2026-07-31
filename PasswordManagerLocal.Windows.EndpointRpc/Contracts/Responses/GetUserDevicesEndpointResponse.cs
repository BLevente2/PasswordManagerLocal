using PasswordManagerLocal.Contracts.Responses;

namespace PasswordManagerLocal.Windows.EndpointRpc.Contracts.Responses;

public sealed class GetUserDevicesEndpointResponse
{
    public IReadOnlyList<UserDeviceInfoResponse> Devices { get; set; } = [];
}
