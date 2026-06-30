using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;
using PasswordManagerLocalBackend.Security;
using System.Security.Cryptography;
using static PasswordManagerLocalBackend.Constants.PasswordConstants;
using PasswordManagerLocalBackend.Utils;

namespace PasswordManagerLocalBackend.Services;

public sealed class PasswordService : IPasswordService
{
    public IReadOnlyList<PasswordInfoResponse> ConvertToPasswordInfoResponses(UserPasswordsData passwords)
    {
        passwords.VerifyIntegrity();

        return passwords.Passwords
            .OrderBy(password => password.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(password => password.Id)
            .Select(PasswordInfoResponse.ConvertToPasswordInfoResponse)
            .ToList();
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

        var now = DateTime.UtcNow;
        var securePassword = new SecurePassword
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            Description = request.Description,
            Color = NormalizeColorCode(request.Color),
            Password = await EncryptPasswordAsync(request.Password, passwords),
            CreatedAt = now,
            LastUpdatedAt = now
        };
        securePassword.GenerateIntegrityHash();

        passwords.DeletedPasswords.RemoveAll(deleted => deleted.Id == securePassword.Id);
        passwords.Passwords.Add(securePassword);
        passwords.GenerateIntegrityHash();
    }


    public void RemovePassword(Guid passwordId, UserPasswordsData passwords)
    {
        using var password = GetAndVerifyPasswordById(passwordId, passwords);
        TombstoneCleanupUtil.AddOrUpdateDeletedPassword(passwords, password.Id, DateTime.UtcNow);
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
            password.Color = NormalizeColorCode(request.Color);

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


    public async Task ExportPasswordsAsync(
        IReadOnlyList<Guid> passwordIds,
        UserPasswordsData sourcePasswords,
        UserPasswordsData targetPasswords)
    {
        sourcePasswords.VerifyIntegrity();
        targetPasswords.VerifyIntegrity();

        if (passwordIds.Count == 0 || passwordIds.Count > MaxNumberOfPasswords ||
            passwordIds.Any(id => id == Guid.Empty) ||
            passwordIds.Distinct().Count() != passwordIds.Count)
            throw new InvalidInputException(["PasswordIds"]);

        if (targetPasswords.Passwords.Count + passwordIds.Count > MaxNumberOfPasswords)
            throw new LimitReachedException(MaxNumberOfPasswords, "password");

        var selectedPasswords = new List<SecurePassword>(passwordIds.Count);
        foreach (var passwordId in passwordIds)
        {
            var password = sourcePasswords.Passwords.FirstOrDefault(pw => pw.Id == passwordId);
            if (password is null)
                throw new PasswordNotFoundException(passwordId);

            password.VerifyIntegrity();
            selectedPasswords.Add(password);
        }

        var targetPasswordNames = new HashSet<string>(
            targetPasswords.Passwords.Select(password => NormalizePasswordName(password.Name)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var password in selectedPasswords)
        {
            var normalizedName = NormalizePasswordName(password.Name);
            if (!targetPasswordNames.Add(normalizedName))
                throw new DuplicatePasswordNameException(normalizedName);
        }

        var now = DateTime.UtcNow;
        var copiedPasswords = new List<SecurePassword>(selectedPasswords.Count);

        try
        {
            foreach (var sourcePassword in selectedPasswords)
            {
                byte[] rawPassword = [];
                try
                {
                    rawPassword = await DecryptPasswordAsync(sourcePassword.Password, sourcePasswords);

                    var copiedPassword = new SecurePassword
                    {
                        Id = Guid.NewGuid(),
                        Name = NormalizePasswordName(sourcePassword.Name),
                        Description = sourcePassword.Description,
                        Color = NormalizeColorCode(sourcePassword.Color),
                        Password = await EncryptPasswordAsync(rawPassword, targetPasswords),
                        CreatedAt = now,
                        LastUpdatedAt = now
                    };
                    copiedPassword.GenerateIntegrityHash();
                    copiedPasswords.Add(copiedPassword);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(rawPassword);
                }
            }

            foreach (var copiedPassword in copiedPasswords)
            {
                targetPasswords.DeletedPasswords.RemoveAll(deleted => deleted.Id == copiedPassword.Id);
                targetPasswords.Passwords.Add(copiedPassword);
            }

            targetPasswords.GenerateIntegrityHash();
        }
        catch
        {
            var copiedIds = copiedPasswords.Select(password => password.Id).ToHashSet();
            targetPasswords.Passwords.RemoveAll(password => copiedIds.Contains(password.Id));

            foreach (var copiedPassword in copiedPasswords)
                copiedPassword.Dispose();

            throw;
        }
    }


    private static string NormalizePasswordName(string name) => name.Trim();


    private static string NormalizeColorCode(string colorCode) => colorCode.Trim().ToUpperInvariant();


    private static void ThrowIfPasswordNameExists(string name, UserPasswordsData passwords, Guid? ignoredPasswordId = null)
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
