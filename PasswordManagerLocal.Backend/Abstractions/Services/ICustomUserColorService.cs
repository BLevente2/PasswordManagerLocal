using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Requests;
using PasswordManagerLocal.Backend.Responses;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface ICustomUserColorService
{
    IReadOnlyList<CustomUserColorInfoResponse> ConvertToCustomUserColorInfoResponses(UserPasswordsData passwords);
    void AddCustomUserColor(NewCustomUserColorRequest request, UserPasswordsData passwords);
    void AddCustomUserColors(IReadOnlyList<NewCustomUserColorRequest> requests, UserPasswordsData passwords);
    void DeleteCustomUserColor(Guid customUserColorId, UserPasswordsData passwords);
    void UpdateCustomUserColor(UpdateCustomUserColorRequest request, UserPasswordsData passwords);
    CustomUserColor GetAndVerifyCustomUserColorById(Guid customUserColorId, UserPasswordsData passwords);
}
