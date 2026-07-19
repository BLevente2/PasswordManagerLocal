using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;

namespace PasswordManagerLocal.Test.Fakes;

public sealed record FakeUserDataRecoveryCall(
    Guid UserId,
    UserSyncKeyConfidence KeyConfidence,
    UserDataRecoveryTrigger Trigger);
