using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IPasswordService
{
    IReadOnlyList<PasswordInfoResponse> ConvertToPasswordInfoRespponses(SecurePasswords passwords);
    Task AddNewPassword(NewPasswordRequest request, SecurePasswords passwords);
    void RemovePassword(Guid passwordId, SecurePasswords passwords);
    SecurePassword GetAndVerifyPasswordById(Guid passwordId, SecurePasswords passwords);
    Task<byte[]> GetUnsecurePasswordAsync(Guid passwordId, SecurePasswords passwords);
    Task UpdatePasswordAsync(UpdatePasswordRequest request, SecurePasswords passwords);
    Task<byte[]> EncryptPasswordAsync(byte[] raw, SecurePasswords passwords);
    Task<byte[]> DecryptPasswordAsync(byte[] password, SecurePasswords passwords);
}