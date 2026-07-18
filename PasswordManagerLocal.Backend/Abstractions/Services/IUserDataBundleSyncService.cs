using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserDataBundleSyncService
{
    Task<UserDataBundleMergeResult> TryMergeAsync(User existing, UserSyncPayload incoming, long ts, CancellationToken ct = default);
}
