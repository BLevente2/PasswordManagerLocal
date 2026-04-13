using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Sync;

namespace PasswordManagerLocalBackend.Services;

public sealed class IncomingDeltaApplierService : IIncomingDeltaApplierService
{
    private readonly IUserRepository _userRepository;
    private readonly IGroupRepository _groupRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly ISyncQueueRepository _syncQueueRepository;

    public IncomingDeltaApplierService(
        IUserRepository userRepository,
        IGroupRepository groupRepository,
        IDeviceRepository deviceRepository,
        ISyncQueueRepository syncQueueRepository)
    {
        _userRepository = userRepository;
        _groupRepository = groupRepository;
        _deviceRepository = deviceRepository;
        _syncQueueRepository = syncQueueRepository;
    }

    public Task<long> ApplyAsync(NetworkDelta delta, CancellationToken ct = default)
    {
        throw new NotImplementedException("Incoming sync delta application is not implemented yet.");
    }
}
