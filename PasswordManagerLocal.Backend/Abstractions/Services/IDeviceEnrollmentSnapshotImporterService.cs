using PasswordManagerLocal.Backend.Sync;

using PasswordManagerLocal.Backend.Sync.Enrollment;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IDeviceEnrollmentSnapshotImporterService
{
    Task ImportAsync(IServiceProvider services, DeviceEnrollmentSnapshot snapshot, CancellationToken ct = default);
}
