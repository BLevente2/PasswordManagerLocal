using PasswordManagerLocalBackend.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace PasswordManagerLocalBackend.Abstractions.Repositories;

public interface ISyncItemRepository : IGenericRepository<SyncItem>
{
    Task<int> ClearSyncItemsAsync(CancellationToken ct = default);
    Task<SyncItem?> GetAsync(Guid modelId, SyncModelType modelType, CancellationToken ct = default);
}