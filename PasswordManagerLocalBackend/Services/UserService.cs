using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Services;

public sealed class UserService : IUserService
{
    private readonly IUserRepository _users;

    public UserService(IUserRepository users)
    {
        _users = users;
    }




    public async Task<UserData> GetUserDataAsync(User user, string token)
    {
        throw new NotImplementedException();
    }


    public async Task<UserData> GetUserDataSync(User user, string token, EncryptionKey key)
    {
        throw new NotImplementedException();
    }
}