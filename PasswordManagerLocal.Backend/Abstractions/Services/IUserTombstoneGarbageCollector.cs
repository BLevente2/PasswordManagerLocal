using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserTombstoneGarbageCollector
{
    Task<TombstoneGarbageCollectionResult> CollectAsync(Guid userId, CancellationToken ct = default);
    Task<TombstoneGarbageCollectionResult> CollectAsync(Guid userId, EncryptionKey key, CancellationToken ct = default);
}
