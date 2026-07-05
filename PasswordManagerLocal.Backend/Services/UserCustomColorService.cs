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


    public async Task DeleteCustomUserColorsAsync(
        Guid token,
        IReadOnlyList<Guid> customUserColorIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(customUserColorIds);

        var bundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(token, ct);

        if (customUserColorIds.Count == 0)
            return;

        _customUserColorService.DeleteCustomUserColors(customUserColorIds, bundle.UserPasswordsData);
        await _userService.UpdateUserDataBundleAsync(bundle, token, UserDataBlobKind.Passwords, true, ct);
    }


    public async Task UpdateCustomUserColorAsync(Guid token, UpdateCustomUserColorRequest request, CancellationToken ct = default)
    {
        var bundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(token, ct);
        _customUserColorService.UpdateCustomUserColor(request, bundle.UserPasswordsData);
        await _userService.UpdateUserDataBundleAsync(bundle, token, UserDataBlobKind.Passwords, true, ct);
    }
}
