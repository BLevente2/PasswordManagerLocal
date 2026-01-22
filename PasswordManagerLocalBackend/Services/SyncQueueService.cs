using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;

namespace PasswordManagerLocalBackend.Services;

public sealed class SyncQueueService : ISyncQueueService
{
    private readonly IInMemorySyncQueueService _inMemorySyncQueue;
    private readonly ISyncQueueRepository _queueRepo;

    public SyncQueueService(IInMemorySyncQueueService inMemorySyncQueue, ISyncQueueRepository queueRepo)
    {
        _inMemorySyncQueue = inMemorySyncQueue;
        _queueRepo = queueRepo;
    }
}