using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Requests;
using PasswordManagerLocal.Backend.Responses;

namespace PasswordManagerLocal.Backend.Services;

public sealed class UserPasswordsService : IUserPasswordsService
{
    private readonly IUserService _userService;
    private readonly IPasswordService _passwordService;
    private readonly ICustomUserColorService _customUserColorService;
    private readonly IPasswordTagService _passwordTagService;

    public UserPasswordsService(
        IUserService userService,
        IPasswordService passwordService,
        ICustomUserColorService customUserColorService,
        IPasswordTagService passwordTagService)
    {
        _userService = userService;
        _passwordService = passwordService;
        _customUserColorService = customUserColorService;
        _passwordTagService = passwordTagService;
    }



    public async Task<SavedPasswordsResponse> GetSavedPasswordsAsync(Guid token, CancellationToken ct = default)
    {
        var bundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(token, ct);
        return new SavedPasswordsResponse
        {
            Passwords = _passwordService.ConvertToPasswordInfoResponses(bundle.UserPasswordsData),
            CustomColors = _customUserColorService.ConvertToCustomUserColorInfoResponses(bundle.UserPasswordsData),
            Tags = _passwordTagService.ConvertToPasswordTagInfoResponses(bundle.UserPasswordsData)
        };
    }


    public async Task AddNewPasswordAsync(Guid token, NewPasswordRequest request, CancellationToken ct = default)
    {
        var bundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(token, ct);
        await _passwordService.AddNewPassword(request, bundle.UserPasswordsData);
        await _userService.UpdateUserDataBundleAsync(bundle, token, UserDataBlobKind.Passwords, true, ct);
    }


    public async Task RemovePasswordsAsync(
        Guid token,
        IReadOnlyList<Guid> passwordIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(passwordIds);

        var bundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(token, ct);

        if (passwordIds.Count == 0)
            return;

        _passwordService.RemovePasswords(passwordIds, bundle.UserPasswordsData);
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

    public async Task ExportPasswordsToUserAsync(
        Guid sourceToken,
        ExportPasswordsToUserRequest request,
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

        await _passwordService.ExportPasswordsAsync(
            request.PasswordIds!,
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

        _passwordService.RemovePasswords(request.PasswordIds!, sourceBundle.UserPasswordsData);
        await _userService.UpdateUserDataBundleAsync(
            sourceBundle,
            sourceToken,
            UserDataBlobKind.Passwords,
            true,
            ct);
    }

}
