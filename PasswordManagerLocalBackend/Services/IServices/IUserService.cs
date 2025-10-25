using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Services.IServices;

public interface IUserService
{

    public Task<User?> GetUserByUId(Guid uid);
    public Task<User?> GetUserByUsername(byte[] username);
    public Task<User> AddUser(User user);
    public Task<User> UpdateUser(User user);
    public Task DeleteUser(User user);


    public Task<User> UpdateUserProfile(UserProfile newUserProfile, User? user = null, EncryptionKey? encryptionKey = null);
    public Task<User> UpdateUsername(Guid uid, string newUsername);

}