using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Contracts.Requests;
using PasswordManagerLocal.Contracts.Responses;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IPasswordTagService
{
    IReadOnlyList<PasswordTagInfoResponse> ConvertToPasswordTagInfoResponses(UserPasswordsData passwords);
    void AddPasswordTag(NewPasswordTagRequest request, UserPasswordsData passwords);
    void DeletePasswordTag(Guid passwordTagId, UserPasswordsData passwords);
    void ExportPasswordTags(IReadOnlyList<Guid> passwordTagIds, UserPasswordsData sourcePasswords, UserPasswordsData targetPasswords);
    void UpdatePasswordTag(UpdatePasswordTagRequest request, UserPasswordsData passwords);
    PasswordTag GetAndVerifyPasswordTagById(Guid passwordTagId, UserPasswordsData passwords);
}
