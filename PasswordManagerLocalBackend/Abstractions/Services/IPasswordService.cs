using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IPasswordService
{
    IReadOnlyList<PasswordInfoResponse> ConvertToPasswordInfoRespponses(UserPasswordsData passwords);
    Task AddNewPassword(NewPasswordRequest request, UserPasswordsData passwords);
    void RemovePassword(Guid passwordId, UserPasswordsData passwords);
    SecurePassword GetAndVerifyPasswordById(Guid passwordId, UserPasswordsData passwords);
    Task<byte[]> GetUnsecurePasswordAsync(Guid passwordId, UserPasswordsData passwords);
    Task UpdatePasswordAsync(UpdatePasswordRequest request, UserPasswordsData passwords);
    Task<byte[]> EncryptPasswordAsync(byte[] raw, UserPasswordsData passwords);
    Task<byte[]> DecryptPasswordAsync(byte[] password, UserPasswordsData passwords);
}
