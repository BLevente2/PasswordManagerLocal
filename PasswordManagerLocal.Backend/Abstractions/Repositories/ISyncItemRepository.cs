using PasswordManagerLocal.Backend.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace PasswordManagerLocal.Backend.Abstractions.Repositories;

public interface ISyncItemRepository : IGenericRepository<SyncItem>
{
    Task<int> ClearSyncItemsAsync(CancellationToken ct = default);
    Task<SyncItem?> GetAsync(Guid modelId, SyncModelType modelType, CancellationToken ct = default);
}