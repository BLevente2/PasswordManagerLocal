using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;
using PasswordManagerLocalBackend.Security;
using System.Security.Cryptography;
using System.Text;
using static PasswordManagerLocalBackend.Utils.DataValidationUtil;

namespace PasswordManagerLocalBackend.Services;

public class UserProfileService : IUserProfileService
{
    private readonly IUserService _userService;
    private readonly IAuthService _authService;

    public UserProfileService(IUserService userService, IAuthService authService)
    {
        _userService = userService;
        _authService = authService;
    }



    public async Task<UserProfileInfoResponse> GetUserProfileInfoAsync(Guid token, CancellationToken ct = default)
    {
        var userData = await _userService.GetLoadAndVerifyUserDataAsync(token, ct);
        return UserProfileInfoResponse.ConvertToUserProfileInfoResponse(userData);
    }


    public async Task DeleteUserAccountAsync(Guid token, byte[] password, CancellationToken ct = default)
    {
        if (!IsValidPassword(password))
            throw new InvalidInputException();

        var user = await _userService.GetAndVerifyUserAsync(token, ct);

        if (!_authService.IsPasswordValid(token, password, user.PasswordSalt))
            throw new InvalidInputException();

        _authService.Logout(token);

        await _userService.DeleteUserAsync(user, ct);
    }


    public async Task ChangeUsernameAsync(Guid token, string newUsername, CancellationToken ct = default)
    {
        if (!IsValidUsername(newUsername))
            throw new InvalidInputException();

        var user = await _userService.GetAndVerifyUserAsync(token, ct);
        var userData = await _userService.GetLoadAndVerifyUserDataAsync(token, ct);

        userData.Username = newUsername;
        var usernameBytes = Encoding.UTF8.GetBytes(newUsername);

        try
        {
            CryptographicOperations.ZeroMemory(user.UsernameSalt);
            CryptographicOperations.ZeroMemory(user.UsernameHash);

            user.UsernameSalt = Hashing.GenerateSalt();
            user.UsernameHash = Hashing.SHA256Hash(usernameBytes, user.UsernameSalt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(usernameBytes);
        }

        using var key = _userService.GetEncryptionKeyFromToken(token);
        await _userService.UpdateUserDataAsync(userData, user, key, ct);
    }


    public async Task UpdateUserProfileInfoAsync(UpdateUserProfileRequest request, CancellationToken ct = default)
    {
        if (!request.Validate(out var errors))
            throw new InvalidInputException(errors);

        var userData = await _userService.GetLoadAndVerifyUserDataAsync(request.Token, ct);

        if (request.NewEamil is not null)
            userData.Email = request.NewEamil;

        if (request.newFirstName is not null)
            userData.FirstName = request.newFirstName;

        if (request.NewLastName is not null)
            userData.LastName = request.NewLastName;

        await _userService.UpdateUserDataAsync(userData, request.Token, ct);
    }
}