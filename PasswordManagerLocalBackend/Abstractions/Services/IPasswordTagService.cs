using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IPasswordTagService
{
    IReadOnlyList<PasswordTagInfoResponse> ConvertToPasswordTagInfoResponses(UserPasswordsData passwords);
    void AddPasswordTag(NewPasswordTagRequest request, UserPasswordsData passwords);
    void DeletePasswordTag(Guid passwordTagId, UserPasswordsData passwords);
    void UpdatePasswordTag(UpdatePasswordTagRequest request, UserPasswordsData passwords);
    PasswordTag GetAndVerifyPasswordTagById(Guid passwordTagId, UserPasswordsData passwords);
}
