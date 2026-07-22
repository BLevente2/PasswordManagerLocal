using PasswordManagerLocal.Backend.Requests;

namespace PasswordManagerLocal.Windows.EndpointRpc.Contracts.Requests;

public sealed class AddCustomUserColorsEndpointRequest
{
    public Guid Token { get; set; }
    public IReadOnlyList<NewCustomUserColorRequest> Requests { get; set; } = [];
}
