using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;
using PasswordManagerLocalBackend.Security;
using System.Security.Cryptography;
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
        passwords.VerifyIntegrity();

        if (!request.Validate(out var errors))
            throw new InvalidInputException(errors);

        if (passwords.Passwords.Count >= MaxNumberOfPasswords)
            throw new LimitReachedException(MaxNumberOfPasswords, "password");

        var securePassword = new SecurePassword
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Color = request.Color,
            Password = await EncryptPasswordAsync(request.Password, passwords),
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow
        };
        securePassword.GenerateIntegrityHash();

        passwords.Passwords.Add(securePassword);
        passwords.GenerateIntegrityHash();
    }


    public void RemovePassword(Guid passwordId, SecurePasswords passwords)
    {
        using var password = GetAndVerifyPasswordById(passwordId, passwords);
        passwords.Passwords.Remove(password);
        passwords.GenerateIntegrityHash();
    }


    public SecurePassword GetAndVerifyPasswordById(Guid passwordId, SecurePasswords passwords)
    {
        passwords.VerifyIntegrity();

        var password = passwords.Passwords.FirstOrDefault(pw => pw.Id == passwordId);
        if (password is null)
            throw new PasswordNotFoundException(passwordId);

        password.VerifyIntegrity();
        return password;
    }


    public async Task<byte[]> GetUnsecurePasswordAsync(Guid passwordId, SecurePasswords passwords)
    {
        var password = GetAndVerifyPasswordById(passwordId, passwords);
        return await DecryptPasswordAsync(password.Password, passwords);
    }


    public async Task UpdatePasswordAsync(UpdatePasswordRequest request, SecurePasswords passwords)
    {
        if (!request.Validate(out var errors))
            throw new InvalidInputException(errors);

        var password = GetAndVerifyPasswordById(request.Id, passwords);

        if (request.Name is not null)
            password.Name = request.Name;

        if (request.Description is not null)
            password.Description = request.Description;

        if (request.Color is not null)
            password.Color = request.Color;

        if (request.Password is not null)
        {
            CryptographicOperations.ZeroMemory(password.Password);
            password.Password = await EncryptPasswordAsync(request.Password, passwords);
        }

        password.LastUpdatedAt = DateTime.UtcNow;
        password.GenerateIntegrityHash();
        passwords.GenerateIntegrityHash();
    }


    public async Task<byte[]> EncryptPasswordAsync(byte[] raw, SecurePasswords passwords)
    {
        using var key = EncryptionKey.FromRaw(passwords.PasswordKey);
        return await AES256.EncryptAsync(raw, key);
    }


    public async Task<byte[]> DecryptPasswordAsync(byte[] password, SecurePasswords passwords)
    {
        using var key = EncryptionKey.FromRaw(passwords.PasswordKey);
        return await AES256.DecryptAsync(password, key);
    }
}