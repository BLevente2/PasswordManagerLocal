using Microsoft.EntityFrameworkCore;
using PasswordManagerLocalBackend.Context;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Security;
using PasswordManagerLocalBackend.Services.IServices;
using System.Security.Cryptography;
using System.Text;

namespace PasswordManagerLocalBackend.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _context;

    public UserService(AppDbContext context)
    {
        _context = context;
    }



    public async Task<User?> GetUserByUId(Guid uid)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.UId == uid);
    }

    public async Task<User?> GetUserByUsername(byte[] username)
    {
        var users = await _context.Users.ToListAsync();
        return users.FirstOrDefault(u => Hashing.Verify(username, u.UsernameSalt, u.UsernameHash, HashAlgorithmName.SHA256));
    }

    public async Task<User> AddUser(User user)
    {
        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();
        return user;
    }

    public async Task<User> UpdateUser(User user)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
        return user;
    }

    public async Task DeleteUser(User user)
    {
        _context.Users.Remove(user);
        await _context.SaveChangesAsync();
    }


    public async Task<User> UpdateUserProfilea(UserProfile newUserProfile, User? user = null, EncryptionKey? encryptionKey = null)
    {
        if (encryptionKey == null)
            encryptionKey = await LoginManager.GetEncryptionKeyAsync(newUserProfile.UId);

        if (newUserProfile.IsIntegrityValid())
            throw new InvalidOperationException("Userdata has not been modified, integrity hash is valid.");

        if (!newUserProfile.IsPasswordsIntegrityValid())
            throw new InvalidOperationException("User data passwords integrity check failed.");

        if (user == null)
        {
            user = await GetUserByUId(newUserData.UId);

            if (user == null)
                throw new InvalidDataException("User not found.");
        }

        newUserData.GenerateIntegrityHash();
        LoginManager.UpdateCacheUserData(user.UId, newUserData);

        user.EncryptedPayloadIV = AES256.GenerateIV();
        user.EncryptedPayload = await DataEncryptionUtils.EncryptAsync<UserData>(newUserData, encryptionKey, user.EncryptedPayloadIV);
        encryptionKey.Dispose();
        user.GenerateIntegrityHash();

        return await UpdateUser(user);
    }

    public async Task<User> UpdateUsername(Guid uid, string newUsername)
    {
        using var enryptionKey = await LoginManager.GetEncryptionKeyAsync(uid);

        var user = await GetUserByUId(uid);

        if (user == null)
            throw new InvalidOperationException("User not found.");

        if (!user.IsIntegrityValid())
            throw new InvalidOperationException("User integrity check failed.");

        byte[] newUsernameBytes = Encoding.UTF8.GetBytes(newUsername);

        var foundUser = await GetUserByUsername(newUsernameBytes);
        bool isUsernameAlreadyTaken = foundUser != null && !string.Equals(foundUser.UId, uid);
        bool isValidUsername = UserDataUtils.IsValidUsername(newUsername);

        if (isUsernameAlreadyTaken || !isValidUsername)
            throw new ArgumentException("Invalid or already taken username.");

        user.UsernameSalt = Hashing.GenerateSalt();
        user.UsernameHash = Hashing.SHA256Hash(newUsernameBytes, user.UsernameSalt);

        return await UpdateUser(user);
    }
}