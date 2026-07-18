using Microsoft.EntityFrameworkCore;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Persistence;

namespace PasswordManagerLocal.Backend.Repositories;

public sealed class UserMembershipAuthorizationRepository : IUserMembershipAuthorizationRepository
{
    private readonly DbSet<UserMembershipAuthorization> _rows;

    public UserMembershipAuthorizationRepository(AppDbContext context) => _rows = context.UserMembershipAuthorizations;

    public Task<UserMembershipAuthorization?> GetByIdAsync(Guid authorizationId, CancellationToken ct = default) =>
        _rows.FirstOrDefaultAsync(row => row.AuthorizationId == authorizationId, ct);

    public Task<UserMembershipAuthorization?> GetForSignedEpochAsync(Guid userId, Guid deviceId, Guid originInstanceId, long membershipEpoch, CancellationToken ct = default) =>
        _rows.FirstOrDefaultAsync(row =>
            row.UserId == userId && row.DeviceId == deviceId && row.OriginInstanceId == originInstanceId &&
            row.StartedMembershipEpoch <= membershipEpoch &&
            (row.EndedMembershipEpoch == null || membershipEpoch < row.EndedMembershipEpoch), ct);

    public Task<UserMembershipAuthorization?> GetActiveAsync(Guid userId, Guid deviceId, Guid originInstanceId, CancellationToken ct = default) =>
        _rows.FirstOrDefaultAsync(row => row.UserId == userId && row.DeviceId == deviceId && row.OriginInstanceId == originInstanceId && row.IsActive, ct);

    public async Task<IReadOnlyList<UserMembershipAuthorization>> ListForUserAsync(Guid userId, CancellationToken ct = default) =>
        await _rows.Where(row => row.UserId == userId)
            .OrderBy(row => row.StartedMembershipEpoch).ThenBy(row => row.DeviceId).ThenBy(row => row.OriginInstanceId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UserMembershipAuthorization>> ListActiveForDeviceAsync(Guid userId, Guid deviceId, CancellationToken ct = default) =>
        await _rows.Where(row => row.UserId == userId && row.DeviceId == deviceId && row.IsActive)
            .OrderBy(row => row.OriginInstanceId).ToListAsync(ct);

    public Task AddAsync(UserMembershipAuthorization authorization, CancellationToken ct = default) =>
        _rows.AddAsync(authorization, ct).AsTask();

    public void Update(UserMembershipAuthorization authorization) => _rows.Update(authorization);
}
