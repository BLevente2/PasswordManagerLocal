using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Serialization;
using PasswordManagerLocal.Windows.Ipc.Validation;

namespace PasswordManagerLocal.Windows.Ipc.Test.Contracts;

[TestClass]
public sealed class SynchronizationStatusContractTests
{
    [TestMethod]
    public void EveryBackendRuntimeStateHasExactlyOneTransportState()
    {
        var backendStates = Enum.GetNames<SyncRuntimeState>();
        var transportStates = Enum.GetNames<SynchronizationStatusState>();

        CollectionAssert.AreEqual(backendStates, transportStates);
        Assert.AreEqual(
            Enum.GetValues<SyncRuntimeState>().Length,
            Enum.GetValues<SynchronizationStatusState>().Length);
    }

    [TestMethod]
    public void DisabledStartingRunningAndStoppingRoundTrip()
    {
        var serializer = new WindowsIpcSerializer();
        var validator = new WindowsIpcContractValidator();
        var states = new[]
        {
            SynchronizationStatusState.Disabled,
            SynchronizationStatusState.Starting,
            SynchronizationStatusState.Running,
            SynchronizationStatusState.Stopping
        };

        foreach (var state in states)
        {
            var original = new SynchronizationStatusDto(
                state,
                null);
            validator.Validate(original);
            var payload = serializer.Serialize(
                original,
                WindowsIpcJsonContext.Default.SynchronizationStatusDto);
            var roundTripped = serializer.Deserialize(
                payload,
                WindowsIpcJsonContext.Default.SynchronizationStatusDto);
            validator.Validate(roundTripped);
            Assert.AreEqual(original, roundTripped);
        }
    }

    [TestMethod]
    public void DegradedStateRoundTripsWithSynchronizationFailure()
    {
        var serializer = new WindowsIpcSerializer();
        var validator = new WindowsIpcContractValidator();
        var original = new SynchronizationStatusDto(
            SynchronizationStatusState.Degraded,
            new IpcFailureDto(
                IpcFailureKind.Synchronization,
                "Synchronization is degraded.",
                DateTimeOffset.UtcNow,
                IsRetryable: true,
                RequiresProcessRestart: false));

        var payload = serializer.Serialize(
            original,
            WindowsIpcJsonContext.Default.SynchronizationStatusDto);
        var roundTripped = serializer.Deserialize(
            payload,
            WindowsIpcJsonContext.Default.SynchronizationStatusDto);

        validator.Validate(roundTripped);
        Assert.AreEqual(original, roundTripped);
    }

    [TestMethod]
    public void ContractContainsNoInventedSchedulingOrTransferTimestamps()
    {
        var propertyNames = typeof(SynchronizationStatusDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[] { "State", "LastFailure" },
            propertyNames);
    }
}
