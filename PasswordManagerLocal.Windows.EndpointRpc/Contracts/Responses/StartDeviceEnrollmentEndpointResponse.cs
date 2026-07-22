using PasswordManagerLocal.Backend.Responses;

namespace PasswordManagerLocal.Windows.EndpointRpc.Contracts.Responses;

public sealed class StartDeviceEnrollmentEndpointResponse
{
    public DeviceEnrollmentCodeResponse Enrollment { get; set; } = new();
}
