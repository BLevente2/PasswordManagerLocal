using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalBackend.Services;

public sealed class UserPasswordsService : IUserPasswordsService
{
    private readonly IUserService _userService;
    private readonly IPasswordService _passwordService;
    private readonly ICustomUserColorService _customUserColorService;

    public UserPasswordsService(
        IUserService userService,
        IPasswordService passwordService,
        ICustomUserColorService customUserColorService)
    {
        _userService = userService;
        _passwordService = passwordService;
        _customUserColorService = customUserColorService;
    }



    public async Task<SavedPasswordsResponse> GetSavedPasswordsAsync(Guid token, CancellationToken ct = default)
    {
        var bundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(token, ct);
        return new SavedPasswordsResponse
        {
            Passwords = _passwordService.ConvertToPasswordInfoResponses(bundle.UserPasswordsData),
            CustomColors = _customUserColorService.ConvertToCustomUserColorInfoResponses(bundle.UserPasswordsData)
        };
    }


    public async Task AddNewPasswordAsync(Guid token, NewPasswordRequest request, CancellationToken ct = default)
    {
        var bundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(token, ct);
        await _passwordService.AddNewPassword(request, bundle.UserPasswordsData);
        await _userService.UpdateUserDataBundleAsync(bundle, token, UserDataBlobKind.Passwords, true, ct);
    }


    public async Task RemovePasswordAsync(Guid token, Guid passwordId, CancellationToken ct = default)
    {
        var bundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(token, ct);
        _passwordService.RemovePassword(passwordId, bundle.UserPasswordsData);
        await _userService.UpdateUserDataBundleAsync(bundle, token, UserDataBlobKind.Passwords, true, ct);
    }


    public async Task<byte[]> GetUnsecurePasswordAsync(Guid token, Guid passwordId, CancellationToken ct = default)
    {
        var bundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(token, ct);
        return await _passwordService.GetUnsecurePasswordAsync(passwordId, bundle.UserPasswordsData);
    }


    public async Task UpdatePasswordAsync(Guid token, UpdatePasswordRequest request, CancellationToken ct = default)
    {
        var bundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(token, ct);
        await _passwordService.UpdatePasswordAsync(request, bundle.UserPasswordsData);
        await _userService.UpdateUserDataBundleAsync(bundle, token, UserDataBlobKind.Passwords, true, ct);
    }
}
