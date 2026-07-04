using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Abstractions.Repositories;

public interface IDeviceIdentityRepository
{
    Task<LocalDeviceIdentity?> Get(CancellationToken ct = default);
    Task Create(LocalDeviceIdentity identity, CancellationToken ct = default);
    void Update(LocalDeviceIdentity identity);
}