using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeUserDataRecoveryScheduler : IUserDataRecoveryScheduler
{
    public List<(Guid UserId, UserDataRecoveryTrigger Trigger)> Calls { get; } = [];

    public void Schedule(Guid userId, UserDataRecoveryTrigger trigger) =>
        Calls.Add((userId, trigger));
}
