using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalBackend.Services;

public sealed class UserPasswordsService : IUserPasswordsService
{
    private readonly IUserService _userService;
    private readonly IPasswordService _passwordService;

    public UserPasswordsService(IUserService userService, IPasswordService passwordService)
    {
        _userService = userService;
        _passwordService = passwordService;
    }



    public async Task<IReadOnlyList<PasswordInfoResponse>> GetSavedPasswordsAsync(Guid token, CancellationToken ct = default)
    {
        UserData userData = await _userService.GetLoadAndVerifyUserDataAsync(token, ct);
        return _passwordService.ConvertToPasswordInfoRespponses(userData.Passwords);
    }


    public async Task AddNewPasswordAsync(Guid token, NewPasswordRequest request, CancellationToken ct = default)
    {
        UserData userData = await _userService.GetLoadAndVerifyUserDataAsync(token, ct);
        await _passwordService.AddNewPassword(request, userData.Passwords);
        await _userService.UpdateUserDataAsync(userData, token, true, ct);
    }


    public async Task RemovePasswordAsync(Guid token, Guid passwordId, CancellationToken ct = default)
    {
        var userData = await _userService.GetLoadAndVerifyUserDataAsync(token, ct);
        _passwordService.RemovePassword(passwordId, userData.Passwords);
        await _userService.UpdateUserDataAsync(userData, token, true, ct);
    }


    public async Task<byte[]> GetUnsecurePasswordAsync(Guid token, Guid passwordId, CancellationToken ct = default)
    {
        var userData = await _userService.GetLoadAndVerifyUserDataAsync(token, ct);
        return await _passwordService.GetUnsecurePasswordAsync(passwordId, userData.Passwords);
    }


    public async Task UpdatePasswordAsync(Guid token, UpdatePasswordRequest request, CancellationToken ct = default)
    {
        var userData = await _userService.GetLoadAndVerifyUserDataAsync(token, ct);
        await _passwordService.UpdatePasswordAsync(request, userData.Passwords);
        await _userService.UpdateUserDataAsync(userData, token, true, ct);
    }
}