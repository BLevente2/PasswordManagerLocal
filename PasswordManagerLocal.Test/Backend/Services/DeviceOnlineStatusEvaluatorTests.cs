using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Sync.Discovery;
using PasswordManagerLocal.Test.Fakes;

namespace PasswordManagerLocal.Test.Backend.Services;

[TestClass]
public sealed class DeviceOnlineStatusEvaluatorTests
{
    [TestMethod]
    public void InteractiveProfile_UsesThirtyFiveSecondBoundary()
    {
        var provider = CreateProvider(TimeSpan.FromSeconds(35), isInteractive: true);
        var registry = new RecordingDiscoveredDeviceEndpointRegistry();
        var evaluator = new DeviceOnlineStatusEvaluator(provider, registry);

        evaluator.IsRecentlyDiscovered("AA");

        Assert.AreEqual(TimeSpan.FromSeconds(35), registry.LastMaximumAge);
    }

    [TestMethod]
    public void BackgroundProfile_UsesOneHundredTwentyFiveSecondBoundary()
    {
        var provider = CreateProvider(TimeSpan.FromSeconds(125), isInteractive: false);
        var registry = new RecordingDiscoveredDeviceEndpointRegistry();
        var evaluator = new DeviceOnlineStatusEvaluator(provider, registry);

        evaluator.IsRecentlyDiscovered("AA");

        Assert.AreEqual(TimeSpan.FromSeconds(125), registry.LastMaximumAge);
    }

    [TestMethod]
    public void ProfileChange_SubsequentQueryUsesNewTimeout()
    {
        var provider = CreateProvider(TimeSpan.FromSeconds(35), isInteractive: true);
        var registry = new RecordingDiscoveredDeviceEndpointRegistry();
        var evaluator = new DeviceOnlineStatusEvaluator(provider, registry);
        evaluator.IsRecentlyDiscovered("AA");

        provider.SetProfile(
            new BackendExecutionProfile(
                TimeSpan.FromSeconds(60),
                TimeSpan.FromSeconds(60),
                TimeSpan.FromSeconds(125)),
            isInteractive: false);
        evaluator.IsRecentlyDiscovered("AA");

        Assert.AreEqual(TimeSpan.FromSeconds(125), registry.LastMaximumAge);
    }

    [TestMethod]
    public void InteractiveBoundary_IsInclusiveAndExpiresAfterThirtyFiveSeconds()
    {
        var now = DateTimeOffset.Parse("2026-07-24T08:00:00+00:00");
        var registry = new DiscoveredDeviceEndpointRegistry(() => now);
        registry.AddOrUpdate(new DiscoveredDeviceEndpoint
        {
            Host = "192.168.1.20",
            Port = 26688,
            TlsCertFingerprint = "AABB"
        });
        var evaluator = new DeviceOnlineStatusEvaluator(
            CreateProvider(TimeSpan.FromSeconds(35), isInteractive: true),
            registry);

        now = now.AddSeconds(35);
        Assert.IsTrue(evaluator.IsRecentlyDiscovered("AA:BB"));
        now = now.AddTicks(1);
        Assert.IsFalse(evaluator.IsRecentlyDiscovered("AA:BB"));
    }

    [TestMethod]
    public void BackgroundBoundary_IsInclusiveAndExpiresAfterOneHundredTwentyFiveSeconds()
    {
        var now = DateTimeOffset.Parse("2026-07-24T08:00:00+00:00");
        var registry = new DiscoveredDeviceEndpointRegistry(() => now);
        registry.AddOrUpdate(new DiscoveredDeviceEndpoint
        {
            Host = "192.168.1.20",
            Port = 26688,
            TlsCertFingerprint = "AABB"
        });
        var evaluator = new DeviceOnlineStatusEvaluator(
            CreateProvider(TimeSpan.FromSeconds(125), isInteractive: false),
            registry);

        now = now.AddSeconds(125);
        Assert.IsTrue(evaluator.IsRecentlyDiscovered("AA:BB"));
        now = now.AddTicks(1);
        Assert.IsFalse(evaluator.IsRecentlyDiscovered("AA:BB"));
    }

    [TestMethod]
    public void NoActiveProfile_IsOfflineWithoutRegistryQuery()
    {
        var provider = new FakeBackendExecutionProfileProvider();
        var registry = new RecordingDiscoveredDeviceEndpointRegistry
        {
            RecentlyDiscoveredResult = true
        };
        var evaluator = new DeviceOnlineStatusEvaluator(provider, registry);

        var online = evaluator.IsRecentlyDiscovered("AA");

        Assert.IsFalse(online);
        Assert.IsNull(registry.LastMaximumAge);
    }

    private static FakeBackendExecutionProfileProvider CreateProvider(TimeSpan timeout, bool isInteractive)
    {
        var provider = new FakeBackendExecutionProfileProvider();
        provider.SetProfile(
            new BackendExecutionProfile(
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(15),
                timeout),
            isInteractive);
        return provider;
    }
}
