using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeUserDataRecoveryCoordinator : IUserDataRecoveryCoordinator
{
    public List<RecoveryCall> Calls { get; } = [];
    public UserDataRecoveryResult Result { get; set; } =
        new(UserDataRecoveryState.NothingToRecover, DiagnosticCode: "fake-no-recovery-needed");
    public byte[]? PasswordSalt { get; set; }

    public Task<UserDataRecoveryResult> TryRecoverAsync(
        Guid userId,
        EncryptionKey key,
        UserSyncKeyConfidence keyConfidence,
        UserDataRecoveryTrigger trigger,
        CancellationToken ct = default)
    {
        Calls.Add(new RecoveryCall(userId, keyConfidence, trigger));
        return Task.FromResult(Result);
    }

    public Task<byte[]?> TryResolvePasswordSaltAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(PasswordSalt?.ToArray());

    public sealed record RecoveryCall(
        Guid UserId,
        UserSyncKeyConfidence KeyConfidence,
        UserDataRecoveryTrigger Trigger);
}
