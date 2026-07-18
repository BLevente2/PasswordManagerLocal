using Microsoft.EntityFrameworkCore;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Persistence;

namespace PasswordManagerLocal.Backend.Repositories;

public sealed class DeviceEnrollmentCommitRepository : IDeviceEnrollmentCommitRepository
{
    private readonly DbSet<DeviceEnrollmentCommit> _rows;
    public DeviceEnrollmentCommitRepository(AppDbContext context) => _rows = context.DeviceEnrollmentCommits;

    public Task<DeviceEnrollmentCommit?> GetByIdAsync(Guid commitId, CancellationToken ct = default) => _rows.FirstOrDefaultAsync(row => row.CommitId == commitId, ct);

    public Task<DeviceEnrollmentCommit?> GetRecoverableAsync(Guid userId, Guid targetDeviceId, Guid targetOriginInstanceId, CancellationToken ct = default) =>
        _rows.FirstOrDefaultAsync(row => row.UserId == userId && row.TargetDeviceId == targetDeviceId && row.TargetOriginInstanceId == targetOriginInstanceId &&
            row.Status != DeviceEnrollmentCommitStatus.Revoked, ct);

    public async Task<IReadOnlyList<DeviceEnrollmentCommit>> ListForUserAsync(Guid userId, CancellationToken ct = default) =>
        await _rows.Where(row => row.UserId == userId).OrderByDescending(row => row.CreatedAtUtc).ToListAsync(ct);

    public Task AddAsync(DeviceEnrollmentCommit commit, CancellationToken ct = default) => _rows.AddAsync(commit, ct).AsTask();
    public void Update(DeviceEnrollmentCommit commit) => _rows.Update(commit);
}
