using Microsoft.EntityFrameworkCore;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Persistence;

namespace PasswordManagerLocal.Backend.Repositories;

public sealed class UserControlOperationRepository : IUserControlOperationRepository
{
    private readonly AppDbContext _context;
    private readonly DbSet<UserControlOperation> _operations;

    public UserControlOperationRepository(AppDbContext context)
    {
        _context = context;
        _operations = context.UserControlOperations;
    }

    public Task<UserControlOperation?> GetByIdAsync(Guid operationId, CancellationToken ct = default) =>
        _operations.FirstOrDefaultAsync(operation => operation.OperationId == operationId, ct);

    public Task<UserControlOperation?> GetByOriginSequenceAsync(
        Guid userId,
        Guid originDeviceId,
        Guid originInstanceId,
        long originSequence,
        CancellationToken ct = default) =>
        _operations.FirstOrDefaultAsync(operation =>
            operation.UserId == userId &&
            operation.OriginDeviceId == originDeviceId &&
            operation.OriginInstanceId == originInstanceId &&
            operation.OriginSequence == originSequence, ct);

    public async Task<IReadOnlyList<UserControlOperation>> ListForUserAsync(Guid userId, CancellationToken ct = default) =>
        await _operations
            .Where(operation => operation.UserId == userId)
            .OrderBy(operation => operation.CreatedAtUtc)
            .ThenBy(operation => operation.OriginDeviceId)
            .ThenBy(operation => operation.OriginInstanceId)
            .ThenBy(operation => operation.OriginSequence)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UserControlOperation>> ListAllRelayableAsync(CancellationToken ct = default) =>
        await _operations
            .Where(operation => operation.Status != UserControlOperationStatus.Rejected)
            .OrderBy(operation => operation.UserId)
            .ThenBy(operation => operation.OriginDeviceId)
            .ThenBy(operation => operation.OriginInstanceId)
            .ThenBy(operation => operation.OriginSequence)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UserControlOperation>> ListKeyTransitionsFromAsync(
        Guid userId,
        long previousKeyEpoch,
        CancellationToken ct = default) =>
        await _operations
            .Where(operation =>
                operation.UserId == userId &&
                operation.OperationType == UserControlOperationType.KeyEpochReplacement &&
                operation.PreviousKeyEpoch == previousKeyEpoch &&
                operation.Status != UserControlOperationStatus.Rejected)
            .OrderBy(operation => operation.OperationId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UserControlOperation>> ListMembershipTransitionsFromAsync(
        Guid userId,
        long previousMembershipEpoch,
        CancellationToken ct = default) =>
        await _operations
            .Where(operation => operation.UserId == userId &&
                (operation.OperationType == UserControlOperationType.DeviceAddition || operation.OperationType == UserControlOperationType.DeviceRemoval) &&
                operation.PreviousMembershipEpoch == previousMembershipEpoch &&
                operation.Status != UserControlOperationStatus.Rejected)
            .OrderBy(operation => operation.OperationId)
            .ToListAsync(ct);

    public async Task<long> GetHighestOriginSequenceAsync(
        Guid userId,
        Guid originDeviceId,
        Guid originInstanceId,
        CancellationToken ct = default) =>
        await _operations
            .Where(operation => operation.UserId == userId && operation.OriginDeviceId == originDeviceId && operation.OriginInstanceId == originInstanceId)
            .Select(operation => (long?)operation.OriginSequence)
            .MaxAsync(ct) ?? 0;

    public Task AddAsync(UserControlOperation operation, CancellationToken ct = default) =>
        _operations.AddAsync(operation, ct).AsTask();

    public void Update(UserControlOperation operation) => _operations.Update(operation);

    public void Detach(UserControlOperation operation) =>
        _context.Entry(operation).State = EntityState.Detached;
}
