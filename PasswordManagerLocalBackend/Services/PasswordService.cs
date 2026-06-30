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
    public IReadOnlyList<PasswordInfoResponse> ConvertToPasswordInfoRespponses(UserPasswordsData passwords)
    {
        passwords.VerifyIntegrity();
        var passwordInfos = new List<PasswordInfoResponse>();
        passwords.Passwords.ForEach(pw => passwordInfos.Add(PasswordInfoResponse.ConvertToPasswordInfoResponse(pw)));
        return passwordInfos;
    }


    public async Task AddNewPassword(NewPasswordRequest request, UserPasswordsData passwords)
    {
        passwords.VerifyIntegrity();

        if (!request.Validate(out var errors))
            throw new InvalidInputException(errors);

        if (passwords.Passwords.Count >= MaxNumberOfPasswords)
            throw new LimitReachedException(MaxNumberOfPasswords, "password");

        var normalizedName = NormalizePasswordName(request.Name);
        ThrowIfPasswordNameExists(normalizedName, passwords);

        var securePassword = new SecurePassword
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            Description = request.Description,
            Color = request.Color,
            Password = await EncryptPasswordAsync(request.Password, passwords),
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow
        };
        securePassword.GenerateIntegrityHash();

        passwords.DeletedPasswords.RemoveAll(deleted => deleted.Id == securePassword.Id);
        passwords.Passwords.Add(securePassword);
        passwords.GenerateIntegrityHash();
    }


    public void RemovePassword(Guid passwordId, UserPasswordsData passwords)
    {
        using var password = GetAndVerifyPasswordById(passwordId, passwords);
        AddOrUpdateDeletedPasswordData(passwords, password.Id, DateTime.UtcNow);
        passwords.Passwords.Remove(password);
        passwords.GenerateIntegrityHash();
    }


    public SecurePassword GetAndVerifyPasswordById(Guid passwordId, UserPasswordsData passwords)
    {
        passwords.VerifyIntegrity();

        var password = passwords.Passwords.FirstOrDefault(pw => pw.Id == passwordId);
        if (password is null)
            throw new PasswordNotFoundException(passwordId);

        password.VerifyIntegrity();
        return password;
    }


    public async Task<byte[]> GetUnsecurePasswordAsync(Guid passwordId, UserPasswordsData passwords)
    {
        var password = GetAndVerifyPasswordById(passwordId, passwords);
        return await DecryptPasswordAsync(password.Password, passwords);
    }


    public async Task UpdatePasswordAsync(UpdatePasswordRequest request, UserPasswordsData passwords)
    {
        if (!request.Validate(out var errors))
            throw new InvalidInputException(errors);

        var password = GetAndVerifyPasswordById(request.Id, passwords);

        if (request.Name is not null)
        {
            var normalizedName = NormalizePasswordName(request.Name);
            ThrowIfPasswordNameExists(normalizedName, passwords, password.Id);
            password.Name = normalizedName;
        }

        if (request.Description is not null)
            password.Description = request.Description;

        if (request.Color is not null)
            password.Color = request.Color;

        if (request.Password is not null)
        {
            CryptographicOperations.ZeroMemory(password.Password);
            password.Password = await EncryptPasswordAsync(request.Password, passwords);
        }

        passwords.DeletedPasswords.RemoveAll(deleted => deleted.Id == password.Id);
        password.LastUpdatedAt = DateTime.UtcNow;
        password.GenerateIntegrityHash();
        passwords.GenerateIntegrityHash();
    }


    private static void AddOrUpdateDeletedPasswordData(UserPasswordsData passwords, Guid passwordId, DateTime deletedAt)
    {
        var tombstone = passwords.DeletedPasswords.FirstOrDefault(deleted => deleted.Id == passwordId);
        if (tombstone is null)
        {
            tombstone = new DeletedPasswordData { Id = passwordId };
            passwords.DeletedPasswords.Add(tombstone);
        }

        if (deletedAt > tombstone.DeletedAt)
            tombstone.DeletedAt = deletedAt;

        tombstone.GenerateIntegrityHash();
    }


    private string NormalizePasswordName(string name) => name.Trim();


    private void ThrowIfPasswordNameExists(string name, UserPasswordsData passwords, Guid? ignoredPasswordId = null)
    {
        var exists = passwords.Passwords.Any(password =>
            (!ignoredPasswordId.HasValue || password.Id != ignoredPasswordId.Value)
            && string.Equals(NormalizePasswordName(password.Name), name, StringComparison.OrdinalIgnoreCase));

        if (exists)
            throw new DuplicatePasswordNameException(name);
    }


    public async Task<byte[]> EncryptPasswordAsync(byte[] raw, UserPasswordsData passwords)
    {
        using var key = EncryptionKey.FromRaw(passwords.PasswordKey);
        return await AES256.EncryptAsync(raw, key);
    }


    public async Task<byte[]> DecryptPasswordAsync(byte[] password, UserPasswordsData passwords)
    {
        using var key = EncryptionKey.FromRaw(passwords.PasswordKey);
        return await AES256.DecryptAsync(password, key);
    }
}