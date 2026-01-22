using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Abstractions.Repositories;

public interface ISyncQueueRepository : IGenericRepository<SyncItem>
{
}