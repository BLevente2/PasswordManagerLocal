using PasswordManagerLocal.Backend.Requests;

namespace PasswordManagerLocal.Windows.EndpointRpc.Contracts.Requests;

public sealed class LoginEndpointRequest
{
    public LoginRequest Request { get; set; } = new();
}
