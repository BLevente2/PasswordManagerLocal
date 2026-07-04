namespace PasswordManagerLocal.Backend.Models;

public sealed class UserLoginLookupData
{
    public Guid UId { get; init; }
    public byte[] UsernameSalt { get; init; } = [];
    public byte[] UsernameHash { get; init; } = [];
}
