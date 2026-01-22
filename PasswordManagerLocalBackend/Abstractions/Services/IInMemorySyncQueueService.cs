namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IInMemorySyncQueueService
{
    bool TryEnqueue(Guid item);
    bool TryDequeue(out Guid item);
    bool IsEmpty();
    int Count();
    int Clear();
    bool Contains(Guid item);
    Guid[] ToArray();
}