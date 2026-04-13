using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Services;

public sealed class SyncService : ISyncService
{
    private readonly ISyncQueueService _queue;
    private readonly IUserRepository _userRepo;
    private readonly IGroupRepository _groupRepo;
    public readonly IDeviceRepository _deviceRepo;

    public SyncService(
        ISyncQueueService queue,
        IUserRepository userRepo,
        IGroupRepository groupRepo,
        IDeviceRepository deviceRepo)
    {
        _queue = queue;
        _userRepo = userRepo;
        _groupRepo = groupRepo;
        _deviceRepo = deviceRepo;
    }

    public Task NeedsSyncAsync(
        Guid modelId,
        SyncModelType modelType,
        SyncChangeType changeType,
        CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
