using PasswordManagerLocal.Backend.Models.Encrypted;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserPasswordsDataMergeService
{
    bool Merge(UserPasswordsData local, UserPasswordsData incoming);
}
