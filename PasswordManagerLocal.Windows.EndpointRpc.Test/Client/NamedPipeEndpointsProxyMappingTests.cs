using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Requests;
using PasswordManagerLocal.Windows.EndpointRpc.Client;
using PasswordManagerLocal.Windows.EndpointRpc.Contracts;
using PasswordManagerLocal.Windows.EndpointRpc.Serialization;
using PasswordManagerLocal.Windows.EndpointRpc.Test.Infrastructure;
using PasswordManagerLocal.Windows.EndpointRpc.Validation;

namespace PasswordManagerLocal.Windows.EndpointRpc.Test.Client;

[TestClass]
public sealed class NamedPipeEndpointsProxyMappingTests
{
    [TestMethod]
    public async Task EveryProxyMethodSendsExactlyItsStableOperationId()
    {
        var remoteError = new EndpointRpcError(
            EndpointRpcErrorCode.BackendFailure,
            EndpointRpcErrorCategory.Internal,
            "The endpoint operation failed.",
            1,
            DateTimeOffset.UtcNow,
            false,
            false);
        var transport = new RecordingEndpointRpcTransport((_, _, _) =>
            Task.FromException<byte[]>(new EndpointRpcRemoteException(remoteError)));
        var proxy = new NamedPipeEndpointsProxy(
            transport,
            new EndpointRpcSerializer(),
            new EndpointRpcContractValidator());

        foreach (var invocation in CreateInvocations(proxy))
            await Assert.ThrowsExactlyAsync<EndpointRpcRemoteException>(invocation);

        CollectionAssert.AreEqual(
            Enum.GetValues<EndpointOperationId>().Cast<object>().ToArray(),
            transport.Operations.Cast<object>().ToArray());
        Assert.HasCount(40, transport.RequestPayloads);
    }

    [TestMethod]
    public async Task PreSendCancellationTransmitsNothing()
    {
        var transport = new RecordingEndpointRpcTransport((_, _, _) => Task.FromResult(Array.Empty<byte>()));
        var proxy = new NamedPipeEndpointsProxy(
            transport,
            new EndpointRpcSerializer(),
            new EndpointRpcContractValidator());
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            proxy.GetLocalDeviceInfoAsync(cancellationSource.Token));
        Assert.IsEmpty(transport.Operations);
    }

    [TestMethod]
    public async Task SideEffectingCancellationAfterSubmissionReportsUnknownOutcomeWithoutReplay()
    {
        var sends = 0;
        var transport = new RecordingEndpointRpcTransport((_, _, _) =>
        {
            sends++;
            return Task.FromException<byte[]>(new OperationCanceledException());
        });
        var proxy = new NamedPipeEndpointsProxy(
            transport,
            new EndpointRpcSerializer(),
            new EndpointRpcContractValidator());

        await Assert.ThrowsExactlyAsync<EndpointOperationOutcomeUnknownException>(() =>
            proxy.LogoutAsync(EndpointRpcTestData.Token));
        Assert.AreEqual(1, sends);
    }


    [TestMethod]
    public async Task SideEffectingMalformedResponseReportsUnknownOutcomeWithoutReplay()
    {
        var sends = 0;
        var transport = new RecordingEndpointRpcTransport((_, _, _) =>
        {
            sends++;
            return Task.FromResult("{}"u8.ToArray());
        });
        var proxy = new NamedPipeEndpointsProxy(
            transport,
            new EndpointRpcSerializer(),
            new EndpointRpcContractValidator());

        await Assert.ThrowsExactlyAsync<EndpointOperationOutcomeUnknownException>(() =>
            proxy.LoginAsync(new LoginRequest
            {
                Username = "ValidUser1",
                Password = EndpointRpcTestData.Password(),
                RememberMe = false
            }));
        Assert.AreEqual(1, sends);
    }


    [TestMethod]
    public async Task StructuredRemoteDisconnectRemainsConclusiveWithoutReplay()
    {
        var sends = 0;
        var transport = new RecordingEndpointRpcTransport((_, _, _) =>
        {
            sends++;
            return Task.FromException<byte[]>(new EndpointRpcRemoteException(
                new EndpointRpcError(
                    EndpointRpcErrorCode.Disconnected,
                    EndpointRpcErrorCategory.Availability,
                    "The endpoint connection is unavailable.",
                    1,
                    DateTimeOffset.UtcNow,
                    true,
                    false)));
        });
        var proxy = new NamedPipeEndpointsProxy(
            transport,
            new EndpointRpcSerializer(),
            new EndpointRpcContractValidator());

        await Assert.ThrowsExactlyAsync<EndpointRpcDisconnectedException>(() =>
            proxy.LogoutAsync(EndpointRpcTestData.Token));
        Assert.AreEqual(1, sends);
    }

    [TestMethod]
    public async Task StructuredRemotePayloadFailureRemainsConclusiveWithoutReplay()
    {
        var sends = 0;
        var transport = new RecordingEndpointRpcTransport((_, _, _) =>
        {
            sends++;
            return Task.FromException<byte[]>(new EndpointRpcRemoteException(
                new EndpointRpcError(
                    EndpointRpcErrorCode.ResponsePayloadTooLarge,
                    EndpointRpcErrorCategory.Validation,
                    "The endpoint response payload exceeds the permitted size.",
                    1,
                    DateTimeOffset.UtcNow,
                    false,
                    false)));
        });
        var proxy = new NamedPipeEndpointsProxy(
            transport,
            new EndpointRpcSerializer(),
            new EndpointRpcContractValidator());

        await Assert.ThrowsExactlyAsync<EndpointRpcRemoteException>(() =>
            proxy.LogoutAsync(EndpointRpcTestData.Token));
        Assert.AreEqual(1, sends);
    }

    [TestMethod]
    public async Task SensitiveCallerBufferIsNotClearedOrIncludedInError()
    {
        var password = EndpointRpcTestData.Password();
        var expected = password.ToArray();
        var transport = new RecordingEndpointRpcTransport((_, _, _) =>
            Task.FromException<byte[]>(new EndpointRpcRemoteException(
                new EndpointRpcError(
                    EndpointRpcErrorCode.ValidationFailed,
                    EndpointRpcErrorCategory.Validation,
                    "The endpoint input is invalid.",
                    1,
                    DateTimeOffset.UtcNow,
                    false,
                    false))));
        var proxy = new NamedPipeEndpointsProxy(
            transport,
            new EndpointRpcSerializer(),
            new EndpointRpcContractValidator());

        var exception = await Assert.ThrowsExactlyAsync<EndpointRpcRemoteException>(() =>
            proxy.DeleteUserAccountAsync(EndpointRpcTestData.Token, password));

        CollectionAssert.AreEqual(expected, password);
        Assert.IsFalse(exception.Message.Contains(Convert.ToBase64String(password), StringComparison.Ordinal));
    }


    [TestMethod]
    public async Task InvalidReadOnlyRemoteResponseIsRejectedBeforeFrontendUse()
    {
        var transport = new RecordingEndpointRpcTransport((_, _, _) =>
            Task.FromResult("{}"u8.ToArray()));
        var proxy = new NamedPipeEndpointsProxy(
            transport,
            new EndpointRpcSerializer(),
            new EndpointRpcContractValidator());

        await Assert.ThrowsExactlyAsync<EndpointRpcPayloadException>(() =>
            proxy.GetLocalDeviceInfoAsync());
    }

    [TestMethod]
    public async Task DisconnectedProxyRejectsNewCallsWithoutReplay()
    {
        var sends = 0;
        var transport = new RecordingEndpointRpcTransport((_, _, _) =>
        {
            sends++;
            return Task.FromResult(Array.Empty<byte>());
        });
        await transport.DisposeAsync();
        var proxy = new NamedPipeEndpointsProxy(
            transport,
            new EndpointRpcSerializer(),
            new EndpointRpcContractValidator());

        await Assert.ThrowsExactlyAsync<EndpointRpcDisconnectedException>(() =>
            proxy.GetLocalDeviceInfoAsync());
        Assert.AreEqual(0, sends);
    }

    private static IReadOnlyList<Func<Task>> CreateInvocations(NamedPipeEndpointsProxy proxy)
    {
        Func<EndpointOperationId, object> request = EndpointRpcTestData.CreateRequest;
        var registration = ((PasswordManagerLocal.Windows.EndpointRpc.Contracts.Requests.RegisterEndpointRequest)request(EndpointOperationId.Register)).Request;
        var login = ((PasswordManagerLocal.Windows.EndpointRpc.Contracts.Requests.LoginEndpointRequest)request(EndpointOperationId.Login)).Request;
        var master = ((PasswordManagerLocal.Windows.EndpointRpc.Contracts.Requests.ChangeMasterPasswordEndpointRequest)request(EndpointOperationId.ChangeMasterPassword)).Request;
        var profile = ((PasswordManagerLocal.Windows.EndpointRpc.Contracts.Requests.UpdateUserProfileInfoEndpointRequest)request(EndpointOperationId.UpdateUserProfileInfo)).Request;
        var newPassword = ((PasswordManagerLocal.Windows.EndpointRpc.Contracts.Requests.AddNewPasswordEndpointRequest)request(EndpointOperationId.AddNewPassword)).Request;
        var updatePassword = ((PasswordManagerLocal.Windows.EndpointRpc.Contracts.Requests.UpdatePasswordEndpointRequest)request(EndpointOperationId.UpdatePassword)).Request;
        var exportPasswords = ((PasswordManagerLocal.Windows.EndpointRpc.Contracts.Requests.ExportPasswordsToUserEndpointRequest)request(EndpointOperationId.ExportPasswordsToUser)).Request;
        var colors = ((PasswordManagerLocal.Windows.EndpointRpc.Contracts.Requests.AddCustomUserColorsEndpointRequest)request(EndpointOperationId.AddCustomUserColors)).Requests;
        var exportColors = ((PasswordManagerLocal.Windows.EndpointRpc.Contracts.Requests.ExportCustomUserColorsToUserEndpointRequest)request(EndpointOperationId.ExportCustomUserColorsToUser)).Request;
        var updateColor = ((PasswordManagerLocal.Windows.EndpointRpc.Contracts.Requests.UpdateCustomUserColorEndpointRequest)request(EndpointOperationId.UpdateCustomUserColor)).Request;
        var newTag = ((PasswordManagerLocal.Windows.EndpointRpc.Contracts.Requests.AddPasswordTagEndpointRequest)request(EndpointOperationId.AddPasswordTag)).Request;
        var exportTags = ((PasswordManagerLocal.Windows.EndpointRpc.Contracts.Requests.ExportPasswordTagsToUserEndpointRequest)request(EndpointOperationId.ExportPasswordTagsToUser)).Request;
        var updateTag = ((PasswordManagerLocal.Windows.EndpointRpc.Contracts.Requests.UpdatePasswordTagEndpointRequest)request(EndpointOperationId.UpdatePasswordTag)).Request;
        var password = EndpointRpcTestData.Password();

        return
        [
            async () => { await proxy.RegisterAsync(registration); },
            async () => { await proxy.LoginAsync(login); },
            async () => { await proxy.RenewAuthSessionAsync(EndpointRpcTestData.Token); },
            async () => { await proxy.LogoutAsync(EndpointRpcTestData.Token); },
            async () => { await proxy.GetAuthSessionStatusAsync(EndpointRpcTestData.Token); },
            async () => { await proxy.ChangeMasterPasswordAsync(master); },
            async () => { await proxy.GetUserProfileInfoAsync(EndpointRpcTestData.Token); },
            async () => { await proxy.DeleteUserAccountAsync(EndpointRpcTestData.Token, password); },
            async () => { await proxy.ChangeUsernameAsync(EndpointRpcTestData.Token, "ValidUser2"); },
            async () => { await proxy.UpdateUserProfileInfoAsync(profile); },
            async () => { await proxy.GetLocalDeviceInfoAsync(); },
            async () => { await proxy.GetLocalUserSyncOnAsync(EndpointRpcTestData.Token); },
            async () => { await proxy.SetLocalUserSyncOnAsync(EndpointRpcTestData.Token, true); },
            async () => { await proxy.SetLocalDeviceNameAsync(EndpointRpcTestData.Token, "Desktop"); },
            async () => { await proxy.GetUserDevicesAsync(EndpointRpcTestData.Token); },
            async () => { await proxy.SetUserDeviceNameAsync(EndpointRpcTestData.Token, EndpointRpcTestData.ItemId, "Laptop"); },
            async () => { await proxy.SetUserDeviceSyncOnAsync(EndpointRpcTestData.Token, EndpointRpcTestData.ItemId, true); },
            async () => { await proxy.UnblockUserDeviceAsync(EndpointRpcTestData.Token, EndpointRpcTestData.ItemId); },
            async () => { await proxy.DisconnectUserDeviceAsync(EndpointRpcTestData.Token, EndpointRpcTestData.ItemId, password); },
            async () => { await proxy.StartDeviceEnrollmentAsync(); },
            async () => { await proxy.GetDeviceEnrollmentStatusAsync(); },
            async () => { await proxy.CancelDeviceEnrollmentAsync(); },
            async () => { await proxy.AddDeviceByCodeAsync(EndpointRpcTestData.Token, "ABCD2345"); },
            async () => { await proxy.RestoreRememberedSessionsAsync(); },
            async () => { await proxy.InitializeRememberMeSessionAsync(EndpointRpcTestData.ItemId); },
            async () => { await proxy.SetRememberMeAsync(EndpointRpcTestData.Token, true); },
            async () => { await proxy.GetSavedPasswordsAsync(EndpointRpcTestData.Token); },
            async () => { await proxy.AddNewPasswordAsync(EndpointRpcTestData.Token, newPassword); },
            async () => { await proxy.RemovePasswordsAsync(EndpointRpcTestData.Token, [EndpointRpcTestData.ItemId]); },
            async () => { await proxy.GetUnsecurePasswordAsync(EndpointRpcTestData.Token, EndpointRpcTestData.ItemId); },
            async () => { await proxy.UpdatePasswordAsync(EndpointRpcTestData.Token, updatePassword); },
            async () => { await proxy.ExportPasswordsToUserAsync(EndpointRpcTestData.Token, exportPasswords); },
            async () => { await proxy.AddCustomUserColorsAsync(EndpointRpcTestData.Token, colors); },
            async () => { await proxy.DeleteCustomUserColorsAsync(EndpointRpcTestData.Token, [EndpointRpcTestData.ItemId]); },
            async () => { await proxy.ExportCustomUserColorsToUserAsync(EndpointRpcTestData.Token, exportColors); },
            async () => { await proxy.UpdateCustomUserColorAsync(EndpointRpcTestData.Token, updateColor); },
            async () => { await proxy.AddPasswordTagAsync(EndpointRpcTestData.Token, newTag); },
            async () => { await proxy.DeletePasswordTagAsync(EndpointRpcTestData.Token, EndpointRpcTestData.ItemId); },
            async () => { await proxy.ExportPasswordTagsToUserAsync(EndpointRpcTestData.Token, exportTags); },
            async () => { await proxy.UpdatePasswordTagAsync(EndpointRpcTestData.Token, updateTag); }
        ];
    }
}
