using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;
using PasswordManagerLocalBackend.Security;
using static PasswordManagerLocalBackend.Constants.PasswordConstants;

namespace PasswordManagerLocalBackend.Services;

public sealed class PasswordService : IPasswordService
{
    public IReadOnlyList<PasswordInfoResponse> ConvertToPasswordInfoRespponses(SecurePasswords passwords)
    {
        passwords.VerifyIntegrity();
        var passwordInfos = new List<PasswordInfoResponse>();
        passwords.Passwords.ForEach(pw => passwordInfos.Add(PasswordInfoResponse.ConvertToPasswordInfoResponse(pw)));
        return passwordInfos;
    }


    public async Task AddNewPassword(NewPasswordRequest request, SecurePasswords passwords)
    {
        if (!request.Validate(out var errors))
            throw new InvalidInputException(errors);

        if (passwords.Passwords.Count >= MaxNumberOfPasswords)
            throw new LimitReachedException(MaxNumberOfPasswords, "password");

        passwords.VerifyIntegrity();

        using var key = EncryptionKey.FromRaw(passwords.PasswordKey);
        var encryptedPassword = await AES256.EncryptAsync(request.Password, key);

        var securePassword = new SecurePassword
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Color = request.Color,
            Password = encryptedPassword,
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow
        };
        securePassword.GenerateIntegrityHash();

        passwords.Passwords.Add(securePassword);
        passwords.GenerateIntegrityHash();
    }
}