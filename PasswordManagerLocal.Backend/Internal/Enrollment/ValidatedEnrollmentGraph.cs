using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using System.Text.Json;

using PasswordManagerLocal.Backend.Internal.Enrollment;

namespace PasswordManagerLocal.Backend.Internal.Enrollment;

internal sealed record ValidatedEnrollmentGraph(
    DeviceEnrollmentUserSnapshot User,
    DeviceEnrollmentControlStateSnapshot ControlState,
    IReadOnlyList<ValidatedControlOperation> ControlOperations,
    IReadOnlyList<ValidatedPendingSnapshot> PendingSnapshots,
    IReadOnlyList<DeviceEnrollmentDeviceSnapshot> CurrentRemoteDevices);
