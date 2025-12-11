namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IRememberMeService
{
    Task<IReadOnlyList<string>> InicializeAllRememberMeAsync(CancellationToken ct = default);
}