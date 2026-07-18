using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeUserMembershipAuthorizationRepository : IUserMembershipAuthorizationRepository
{
    private readonly List<UserMembershipAuthorization> _rows = [];

    public Task<UserMembershipAuthorization?> GetByIdAsync(Guid authorizationId, CancellationToken ct = default) =>
        Task.FromResult(_rows.FirstOrDefault(row => row.AuthorizationId == authorizationId));

    public Task<UserMembershipAuthorization?> GetForSignedEpochAsync(Guid userId, Guid deviceId, Guid originInstanceId, long membershipEpoch, CancellationToken ct = default) =>
        Task.FromResult(_rows.FirstOrDefault(row => row.UserId == userId && row.DeviceId == deviceId && row.OriginInstanceId == originInstanceId &&
            row.StartedMembershipEpoch <= membershipEpoch && (!row.EndedMembershipEpoch.HasValue || membershipEpoch < row.EndedMembershipEpoch.Value)));

    public Task<UserMembershipAuthorization?> GetActiveAsync(Guid userId, Guid deviceId, Guid originInstanceId, CancellationToken ct = default) =>
        Task.FromResult(_rows.FirstOrDefault(row => row.UserId == userId && row.DeviceId == deviceId && row.OriginInstanceId == originInstanceId && row.IsActive));

    public Task<IReadOnlyList<UserMembershipAuthorization>> ListForUserAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<UserMembershipAuthorization>>(_rows.Where(row => row.UserId == userId).ToList());

    public Task<IReadOnlyList<UserMembershipAuthorization>> ListActiveForDeviceAsync(Guid userId, Guid deviceId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<UserMembershipAuthorization>>(_rows.Where(row => row.UserId == userId && row.DeviceId == deviceId && row.IsActive).ToList());

    public Task AddAsync(UserMembershipAuthorization authorization, CancellationToken ct = default)
    {
        _rows.Add(authorization);
        return Task.CompletedTask;
    }

    public void Update(UserMembershipAuthorization authorization) { }
}
