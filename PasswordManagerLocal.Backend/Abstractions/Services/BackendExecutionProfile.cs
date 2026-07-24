namespace PasswordManagerLocal.Backend.Abstractions.Services;

public sealed record BackendExecutionProfile(
    TimeSpan LocalDiscoveryInterval,
    TimeSpan NetworkConfigurationPollInterval,
    TimeSpan DeviceOnlineTimeout);
