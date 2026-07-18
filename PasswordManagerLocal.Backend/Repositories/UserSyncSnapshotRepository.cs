using Microsoft.EntityFrameworkCore;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Persistence;

namespace PasswordManagerLocal.Backend.Repositories;

public sealed class UserSyncSnapshotRepository : IUserSyncSnapshotRepository
{
    private readonly DbSet<UserSyncSnapshot> _snapshots;

    public UserSyncSnapshotRepository(AppDbContext context)
    {
        _snapshots = context.UserSyncSnapshots;
    }

    public Task<UserSyncSnapshot?> GetAsync(Guid userId, Guid originDeviceId, Guid originInstanceId, long userKeyEpoch, CancellationToken ct = default) =>
        _snapshots.FirstOrDefaultAsync(snapshot =>
            snapshot.UserId == userId &&
            snapshot.OriginDeviceId == originDeviceId &&
            snapshot.OriginInstanceId == originInstanceId &&
            snapshot.UserKeyEpoch == userKeyEpoch, ct);

    public Task<UserSyncSnapshot?> GetExactAsync(Guid userId, Guid originDeviceId, Guid originInstanceId, long userKeyEpoch, long originRevision, CancellationToken ct = default) =>
        _snapshots.FirstOrDefaultAsync(snapshot =>
            snapshot.UserId == userId &&
            snapshot.OriginDeviceId == originDeviceId &&
            snapshot.OriginInstanceId == originInstanceId &&
            snapshot.UserKeyEpoch == userKeyEpoch &&
            snapshot.OriginRevision == originRevision, ct);

    public async Task<IReadOnlyList<UserSyncSnapshot>> ListPendingAsync(Guid userId, long userKeyEpoch, long membershipEpoch, CancellationToken ct = default) =>
        await _snapshots
            .Where(snapshot =>
                snapshot.UserId == userId &&
                snapshot.UserKeyEpoch == userKeyEpoch &&
                snapshot.MembershipEpoch == membershipEpoch &&
                snapshot.Status == UserSyncSnapshotStatus.Pending)
            .OrderBy(snapshot => snapshot.OriginDeviceId)
            .ThenBy(snapshot => snapshot.OriginInstanceId)
            .ThenBy(snapshot => snapshot.OriginRevision)
            .ToListAsync(ct);

    public Task<UserSyncSnapshot?> GetLatestLocalAsync(Guid userId, Guid originDeviceId, Guid originInstanceId, long userKeyEpoch, CancellationToken ct = default) =>
        _snapshots.FirstOrDefaultAsync(snapshot =>
            snapshot.UserId == userId &&
            snapshot.OriginDeviceId == originDeviceId &&
            snapshot.OriginInstanceId == originInstanceId &&
            snapshot.UserKeyEpoch == userKeyEpoch &&
            snapshot.Status == UserSyncSnapshotStatus.LocalPublished, ct);

    public Task AddAsync(UserSyncSnapshot snapshot, CancellationToken ct = default) =>
        _snapshots.AddAsync(snapshot, ct).AsTask();

    public void Update(UserSyncSnapshot snapshot) => _snapshots.Update(snapshot);
    public void Delete(UserSyncSnapshot snapshot) => _snapshots.Remove(snapshot);
    public void DeleteRange(IEnumerable<UserSyncSnapshot> snapshots) => _snapshots.RemoveRange(snapshots);
}
