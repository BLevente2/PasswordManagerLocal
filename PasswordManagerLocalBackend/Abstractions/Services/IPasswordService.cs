using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IPasswordService
{
    IReadOnlyList<PasswordInfoResponse> ConvertToPasswordInfoRespponses(SecurePasswords passwords);
    Task AddNewPassword(NewPasswordRequest request, SecurePasswords passwords);
}