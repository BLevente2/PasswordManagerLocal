using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface ICustomUserColorService
{
    IReadOnlyList<CustomUserColorInfoResponse> ConvertToCustomUserColorInfoResponses(UserPasswordsData passwords);
    void AddCustomUserColor(NewCustomUserColorRequest request, UserPasswordsData passwords);
    void DeleteCustomUserColor(Guid customUserColorId, UserPasswordsData passwords);
    void UpdateCustomUserColor(UpdateCustomUserColorRequest request, UserPasswordsData passwords);
    CustomUserColor GetAndVerifyCustomUserColorById(Guid customUserColorId, UserPasswordsData passwords);
}
