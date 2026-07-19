using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSec.Cryptography;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Test.Fakes;
using PasswordManagerLocal.Test.TestInfrastructure;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.TestInfrastructure.Repositories.TestDoubles;

internal sealed class MemoryCheckpointRepository : IUserCanonicalCheckpointRepository
{
    private UserCanonicalCheckpoint? _checkpoint;

    public MemoryCheckpointRepository(UserCanonicalCheckpoint checkpoint) => _checkpoint = checkpoint;

    public Task<UserCanonicalCheckpoint?> GetAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(_checkpoint?.UserId == userId ? _checkpoint : null);

    public Task AddAsync(UserCanonicalCheckpoint checkpoint, CancellationToken ct = default)
    {
        _checkpoint = checkpoint;
        return Task.CompletedTask;
    }

    public void Update(UserCanonicalCheckpoint checkpoint) => _checkpoint = checkpoint;

    public void Delete(UserCanonicalCheckpoint checkpoint) => _checkpoint = null;
}
