using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserDataPersistenceValidator
{
    void EnsureUserDataCanBePersisted(UserData userData, User user);
    void EnsureUserDataBundleCanBePersisted(UserDataBundle bundle, User user);
}
