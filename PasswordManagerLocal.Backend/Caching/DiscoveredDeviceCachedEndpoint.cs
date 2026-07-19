using PasswordManagerLocal.Backend.Sync;
using System.Collections.Concurrent;
using PasswordManagerLocal.Backend.Utils;
using PasswordManagerLocal.Backend.Abstractions.Caching;

namespace PasswordManagerLocal.Backend.Caching;

internal sealed record DiscoveredDeviceCachedEndpoint(DiscoveredDeviceEndpoint Endpoint, DateTimeOffset ObservedAt);
