using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Backend.Sync;
using System.Collections.Concurrent;
using PasswordManagerLocal.Backend.Sync.Discovery;

using PasswordManagerLocal.Backend.Utils;

namespace PasswordManagerLocal.Backend.Internal.Sync;

internal sealed record PendingDeviceSyncStart(DiscoveredDeviceEndpoint Endpoint, Device Device);
