using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Requests;

namespace PasswordManagerLocal.Backend.Services;

public sealed class UserPasswordTagService : IUserPasswordTagService
{
    private readonly IUserService _userService;
    private readonly IPasswordTagService _passwordTagService;

    public UserPasswordTagService(IUserService userService, IPasswordTagService passwordTagService)
    {
        _userService = userService;
        _passwordTagService = passwordTagService;
    }


    public async Task AddPasswordTagAsync(Guid token, NewPasswordTagRequest request, CancellationToken ct = default)
    {
        var bundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(token, ct);
        _passwordTagService.AddPasswordTag(request, bundle.UserPasswordsData);
        await _userService.UpdateUserDataBundleAsync(bundle, token, UserDataBlobKind.Passwords, true, ct);
    }


    public async Task DeletePasswordTagAsync(Guid token, Guid passwordTagId, CancellationToken ct = default)
    {
        var bundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(token, ct);
        _passwordTagService.DeletePasswordTag(passwordTagId, bundle.UserPasswordsData);
        await _userService.UpdateUserDataBundleAsync(bundle, token, UserDataBlobKind.Passwords, true, ct);
    }


    public async Task ExportPasswordTagsToUserAsync(
        Guid sourceToken,
        ExportPasswordTagsToUserRequest request,
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

        _passwordTagService.ExportPasswordTags(
            request.PasswordTagIds!,
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

        foreach (var passwordTagId in request.PasswordTagIds!)
            _passwordTagService.DeletePasswordTag(passwordTagId, sourceBundle.UserPasswordsData);

        await _userService.UpdateUserDataBundleAsync(
            sourceBundle,
            sourceToken,
            UserDataBlobKind.Passwords,
            true,
            ct);
    }


    public async Task UpdatePasswordTagAsync(Guid token, UpdatePasswordTagRequest request, CancellationToken ct = default)
    {
        var bundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(token, ct);
        _passwordTagService.UpdatePasswordTag(request, bundle.UserPasswordsData);
        await _userService.UpdateUserDataBundleAsync(bundle, token, UserDataBlobKind.Passwords, true, ct);
    }
}
