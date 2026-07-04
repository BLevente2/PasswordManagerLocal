using PasswordManagerLocal.Backend.Abstractions.Services;
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


    public async Task UpdatePasswordTagAsync(Guid token, UpdatePasswordTagRequest request, CancellationToken ct = default)
    {
        var bundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(token, ct);
        _passwordTagService.UpdatePasswordTag(request, bundle.UserPasswordsData);
        await _userService.UpdateUserDataBundleAsync(bundle, token, UserDataBlobKind.Passwords, true, ct);
    }
}
