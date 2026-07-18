using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Security;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeUserSnapshotMergeCoordinator : IUserSnapshotMergeCoordinator
{
    public int Calls { get; private set; }
    public bool Result { get; set; }

    public Task<bool> TryMergePendingAsync(Guid userId, EncryptionKey key, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(Result);
    }
}
