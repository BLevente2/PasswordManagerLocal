using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserDataRecoveryScheduler
{
    void Schedule(Guid userId, UserDataRecoveryTrigger trigger);
}
