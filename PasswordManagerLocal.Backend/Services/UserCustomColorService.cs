using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
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


    public async Task ExportCustomUserColorsToUserAsync(
        Guid sourceToken,
        ExportCustomUserColorsToUserRequest request,
        CancellationToken ct = default)
    {
        if (!request.Validate(out var errors))
            throw new InvalidInputException(errors);

        var sourceUid = _userService.GetUidFromToken(sourceToken);
        var targetUid = _userService.GetUidFromToken(request.TargetToken);
        if (sourceUid == targetUid)
            throw new InvalidInputException(["TargetToken"]);

        var sourceBundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(sourceToken, ct);
        var targetBundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(request.TargetToken, ct);

        _customUserColorService.ExportCustomUserColors(
            request.CustomUserColorIds!,
            sourceBundle.UserPasswordsData,
            targetBundle.UserPasswordsData);

        await _userService.UpdateUserDataBundleAsync(
            targetBundle,
            request.TargetToken,
            UserDataBlobKind.Passwords,
            true,
            ct);

        if (!request.DeleteOriginal)
            return;

        _customUserColorService.DeleteCustomUserColors(
            request.CustomUserColorIds!,
            sourceBundle.UserPasswordsData);
        await _userService.UpdateUserDataBundleAsync(
            sourceBundle,
            sourceToken,
            UserDataBlobKind.Passwords,
            true,
            ct);
    }


    public async Task UpdateCustomUserColorAsync(Guid token, UpdateCustomUserColorRequest request, CancellationToken ct = default)
    {
        var bundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(token, ct);
        _customUserColorService.UpdateCustomUserColor(request, bundle.UserPasswordsData);
        await _userService.UpdateUserDataBundleAsync(bundle, token, UserDataBlobKind.Passwords, true, ct);
    }
}
