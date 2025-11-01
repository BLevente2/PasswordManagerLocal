namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface ITokenService
{
    string Issue();
    bool Validate(string token);
    int PurgeExpired();
}