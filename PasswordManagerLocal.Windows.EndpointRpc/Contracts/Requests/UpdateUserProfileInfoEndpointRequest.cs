using PasswordManagerLocal.Backend.Requests;

namespace PasswordManagerLocal.Windows.EndpointRpc.Contracts.Requests;

public sealed class UpdateUserProfileInfoEndpointRequest
{
    public UpdateUserProfileRequest Request { get; set; } = null!;
}
