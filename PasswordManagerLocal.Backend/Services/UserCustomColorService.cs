using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Requests;

namespace PasswordManagerLocal.Backend.Services;

public sealed class UserCustomColorService : IUserCustomColorService
{
    private readonly IUserService _userService;
    private readonly ICustomUserColorService _customUserColorService;

    public UserCustomColorService(IUserService userService, ICustomUserColorService customUserColorService)
    {
        _userService = userService;
        _customUserColorService = customUserColorService;
    }


    public async Task AddCustomUserColorsAsync(
        Guid token,
        IReadOnlyList<NewCustomUserColorRequest> requests,
        CancellationToken ct = default)
    {
        var bundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(token, ct);
        _customUserColorService.AddCustomUserColors(requests, bundle.UserPasswordsData);
        await _userService.UpdateUserDataBundleAsync(bundle, token, UserDataBlobKind.Passwords, true, ct);
    }


    public async Task DeleteCustomUserColorAsync(Guid token, Guid customUserColorId, CancellationToken ct = default)
    {
        var bundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(token, ct);
        _customUserColorService.DeleteCustomUserColor(customUserColorId, bundle.UserPasswordsData);
        await _userService.UpdateUserDataBundleAsync(bundle, token, UserDataBlobKind.Passwords, true, ct);
    }


    public async Task UpdateCustomUserColorAsync(Guid token, UpdateCustomUserColorRequest request, CancellationToken ct = default)
    {
        var bundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(token, ct);
        _customUserColorService.UpdateCustomUserColor(request, bundle.UserPasswordsData);
        await _userService.UpdateUserDataBundleAsync(bundle, token, UserDataBlobKind.Passwords, true, ct);
    }
}
