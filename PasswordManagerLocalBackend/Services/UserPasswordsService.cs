using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalBackend.Services;

public sealed class UserPasswordsService : IUserPasswordsService
{
    private readonly IUserService _userService;

    public UserPasswordsService(IUserService userService)
    {
        _userService = userService;
    }



    public async Task<IReadOnlyList<PasswordInfoResponse>> GetSavedPasswordsAsync(Guid token, CancellationToken ct = default)
    {
        UserData userData = await _userService.GetUserDataAsync(token, ct: ct);

        var passwordInfos = new List<PasswordInfoResponse>();
        userData.Passwords.ForEach(pw => passwordInfos.Add(PasswordInfoResponse.ConvertToPasswordInfoResponse(pw)));

        return passwordInfos;
    }
}