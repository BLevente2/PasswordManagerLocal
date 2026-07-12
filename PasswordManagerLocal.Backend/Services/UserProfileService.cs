using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Requests;
using PasswordManagerLocal.Backend.Responses;
using PasswordManagerLocal.Backend.Security;
using System.Security.Cryptography;
using System.Text;
using static PasswordManagerLocal.Backend.Utils.DataValidationUtil;

namespace PasswordManagerLocal.Backend.Services;

public class UserProfileService : IUserProfileService
{
    private readonly IUserLookupService _lookup;
    private readonly IUserDataReaderService _reader;
    private readonly IUserDataWriterService _writer;
    private readonly IUserSessionService _sessions;
    private readonly IUserDeletionService _deletion;
    private readonly IAuthService _authService;

    public UserProfileService(
        IUserLookupService lookup,
        IUserDataReaderService reader,
        IUserDataWriterService writer,
        IUserSessionService sessions,
        IUserDeletionService deletion,
        IAuthService authService)
    {
        _lookup = lookup;
        _reader = reader;
        _writer = writer;
        _sessions = sessions;
        _deletion = deletion;
        _authService = authService;
    }



    public async Task<UserProfileInfoResponse> GetUserProfileInfoAsync(Guid token, CancellationToken ct = default)
    {
        var user = await _lookup.GetAndVerifyUserAsync(token, ct);
        var bundle = await _reader.GetLoadAndVerifyUserDataBundleAsync(token, ct, user);
        return UserProfileInfoResponse.ConvertToUserProfileInfoResponse(
            bundle.UserData,
            bundle.GeneralUserData,
            user.SavedKey is not null);
    }


    public async Task DeleteUserAccountAsync(Guid token, byte[] password, CancellationToken ct = default)
    {
        if (!IsValidPassword(password))
            throw new InvalidInputException();

        var user = await _lookup.GetAndVerifyUserAsync(token, ct);

        if (!_authService.IsPasswordValid(token, password, user.PasswordSalt))
            throw new InvalidInputException();

        _authService.LogoutUser(user.UId, AuthSessionInvalidationReason.ProfileRemoved);

        await _deletion.DeleteUserAsync(user, true, ct);
    }


    public async Task ChangeUsernameAsync(Guid token, string newUsername, CancellationToken ct = default)
    {
        if (!IsValidUsername(newUsername))
            throw new InvalidInputException();

        var user = await _lookup.GetAndVerifyUserAsync(token, ct);
        var bundle = await _reader.GetLoadAndVerifyUserDataBundleAsync(token, ct, user);
        var usernameBytes = Encoding.UTF8.GetBytes(newUsername);

        try
        {
            var existingUser = await _lookup.GetUserByUsernameAsync(usernameBytes, ct);
            if (existingUser is not null && existingUser.UId != user.UId)
                throw new InvalidInputException();

            bundle.GeneralUserData.Username = newUsername;
            bundle.GeneralUserData.LastUpdatedAt = DateTime.UtcNow;
            CryptographicOperations.ZeroMemory(user.UsernameSalt);
            CryptographicOperations.ZeroMemory(user.UsernameHash);

            user.UsernameSalt = Hashing.GenerateSalt();
            user.UsernameHash = Hashing.SHA256Hash(usernameBytes, user.UsernameSalt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(usernameBytes);
        }

        using var key = _sessions.GetEncryptionKeyFromToken(token);
        await _writer.UpdateUserDataBundleAsync(bundle, user, key, UserDataBlobKind.General, true, ct);
    }


    public async Task UpdateUserProfileInfoAsync(UpdateUserProfileRequest request, CancellationToken ct = default)
    {
        if (!request.Validate(out var errors))
            throw new InvalidInputException(errors);

        var bundle = await _reader.GetLoadAndVerifyUserDataBundleAsync(request.Token, ct);

        if (request.NewEamil is not null)
            bundle.GeneralUserData.Email = request.NewEamil;

        if (request.newFirstName is not null)
            bundle.GeneralUserData.FirstName = request.newFirstName;

        if (request.NewLastName is not null)
            bundle.GeneralUserData.LastName = request.NewLastName;

        bundle.GeneralUserData.LastUpdatedAt = DateTime.UtcNow;

        await _writer.UpdateUserDataBundleAsync(bundle, request.Token, UserDataBlobKind.General, true, ct);
    }
}
