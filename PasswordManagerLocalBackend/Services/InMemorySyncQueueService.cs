using PasswordManagerLocalBackend.Abstractions.Services;
using System.Collections.Concurrent;

namespace PasswordManagerLocalBackend.Services;

public sealed class InMemorySyncQueueService : IInMemorySyncQueueService
{
    private readonly ConcurrentQueue<Guid> queue = new();

    public bool TryEnqueue(Guid item)
    {
        if (queue.Contains(item))
            return false;

        queue.Enqueue(item);
        return true;
    }

    public bool TryDequeue(out Guid item) =>
        queue.TryDequeue(out item);

    public bool IsEmpty() =>
        queue.IsEmpty;

    public int Count() =>
        queue.Count;

    public int Clear()
    {
        int count = 0;
        while (queue.TryDequeue(out _))
            count++;

        return count;
    }

    public bool Contains(Guid item) =>
        queue.Contains(item);

    public Guid[] ToArray() =>
        queue.ToArray();
}