namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IInteractiveSensitiveStateResetter
{
    Task ResetAsync(CancellationToken cancellationToken = default);
}
