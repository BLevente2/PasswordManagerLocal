using Microsoft.EntityFrameworkCore;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Persistence;

namespace PasswordManagerLocal.Backend.Repositories;

public sealed class UserCanonicalCheckpointRepository : IUserCanonicalCheckpointRepository
{
    private readonly DbSet<UserCanonicalCheckpoint> _checkpoints;

    public UserCanonicalCheckpointRepository(AppDbContext context) => _checkpoints = context.UserCanonicalCheckpoints;

    public Task<UserCanonicalCheckpoint?> GetAsync(Guid userId, CancellationToken ct = default) =>
        _checkpoints.FirstOrDefaultAsync(checkpoint => checkpoint.UserId == userId, ct);

    public Task AddAsync(UserCanonicalCheckpoint checkpoint, CancellationToken ct = default) =>
        _checkpoints.AddAsync(checkpoint, ct).AsTask();

    public void Update(UserCanonicalCheckpoint checkpoint) => _checkpoints.Update(checkpoint);
    public void Delete(UserCanonicalCheckpoint checkpoint) => _checkpoints.Remove(checkpoint);
}
