using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Caching;
using PasswordManagerLocalBackend.Sync;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocalTest.Backend.Caching;

[TestClass]
public sealed class DiscoveredDeviceEndpointCacheTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void AddOrUpdate_NormalizesFingerprint_AndReturnsIndependentCopies()
    {
        var cache = new DiscoveredDeviceEndpointCache();
        var source = new DiscoveredDeviceEndpoint
        {
            Host = "192.168.1.25",
            Port = 26688,
            TlsCertFingerprint = "aa:bb:cc"
        };

        cache.AddOrUpdate(source);

        MSTestAssert.IsTrue(cache.TryGetByFingerprint("AABBCC", out var first));
        MSTestAssert.IsNotNull(first);
        MSTestAssert.AreEqual("192.168.1.25", first.Host);
        MSTestAssert.AreNotSame(source, first);

        MSTestAssert.IsTrue(cache.TryGetByFingerprint("aa bb cc", out var second));
        MSTestAssert.IsNotNull(second);
        MSTestAssert.AreEqual("192.168.1.25", second.Host);
        MSTestAssert.AreNotSame(first, second);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void AddOrUpdate_InvalidEndpoint_IsIgnored()
    {
        var cache = new DiscoveredDeviceEndpointCache();

        cache.AddOrUpdate(new DiscoveredDeviceEndpoint
        {
            Host = " ",
            Port = 26688,
            TlsCertFingerprint = "AABB"
        });
        cache.AddOrUpdate(new DiscoveredDeviceEndpoint
        {
            Host = "192.168.1.20",
            Port = 0,
            TlsCertFingerprint = "AABB"
        });
        cache.AddOrUpdate(new DiscoveredDeviceEndpoint
        {
            Host = "192.168.1.20",
            Port = 26688,
            TlsCertFingerprint = " "
        });

        MSTestAssert.IsFalse(cache.TryGetByFingerprint("AABB", out _));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void AddOrUpdate_SameFingerprint_ReplacesEndpoint()
    {
        var cache = new DiscoveredDeviceEndpointCache();

        cache.AddOrUpdate(new DiscoveredDeviceEndpoint
        {
            Host = "192.168.1.20",
            Port = 26688,
            TlsCertFingerprint = "AABB"
        });
        cache.AddOrUpdate(new DiscoveredDeviceEndpoint
        {
            Host = "192.168.1.21",
            Port = 30000,
            TlsCertFingerprint = "aa:bb"
        });

        MSTestAssert.IsTrue(cache.TryGetByFingerprint("AABB", out var endpoint));
        MSTestAssert.IsNotNull(endpoint);
        MSTestAssert.AreEqual("192.168.1.21", endpoint.Host);
        MSTestAssert.AreEqual(30000, endpoint.Port);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void RemoveAndClear_RemoveStoredEndpoints()
    {
        var cache = new DiscoveredDeviceEndpointCache();
        cache.AddOrUpdate(new DiscoveredDeviceEndpoint { Host = "host-a", Port = 1, TlsCertFingerprint = "AA" });
        cache.AddOrUpdate(new DiscoveredDeviceEndpoint { Host = "host-b", Port = 2, TlsCertFingerprint = "BB" });

        MSTestAssert.IsTrue(cache.TryRemove("aa"));
        MSTestAssert.IsFalse(cache.TryGetByFingerprint("AA", out _));
        MSTestAssert.IsFalse(cache.TryRemove("AA"));

        cache.Clear();
        MSTestAssert.IsFalse(cache.TryGetByFingerprint("BB", out _));
    }
}
