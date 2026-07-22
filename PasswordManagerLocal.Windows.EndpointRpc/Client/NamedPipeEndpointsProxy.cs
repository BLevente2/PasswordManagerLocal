using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Requests;
using PasswordManagerLocal.Backend.Responses;
using PasswordManagerLocal.Windows.EndpointRpc.Contracts;
using PasswordManagerLocal.Windows.EndpointRpc.Contracts.Requests;
using PasswordManagerLocal.Windows.EndpointRpc.Contracts.Responses;
using PasswordManagerLocal.Windows.EndpointRpc.Metadata;
using PasswordManagerLocal.Windows.EndpointRpc.Security;
using PasswordManagerLocal.Windows.EndpointRpc.Serialization;
using PasswordManagerLocal.Windows.EndpointRpc.Validation;
using System.Text.Json.Serialization.Metadata;

namespace PasswordManagerLocal.Windows.EndpointRpc.Client;

public sealed class NamedPipeEndpointsProxy : IEndpoints
{
    private readonly IEndpointRpcTransport _transport;
    private readonly EndpointRpcSerializer _serializer;
    private readonly EndpointRpcContractValidator _validator;

    public NamedPipeEndpointsProxy(
        IEndpointRpcTransport transport,
        EndpointRpcSerializer serializer,
        EndpointRpcContractValidator validator)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<Guid> RegisterAsync(RegistrationRequest request, CancellationToken ct = default)
    {
        var response = await InvokeAsync(
            EndpointOperationId.Register,
            new RegisterEndpointRequest { Request = EndpointSensitiveData.Clone(request) },
            EndpointRpcJsonContext.Default.RegisterEndpointRequest,
            EndpointRpcJsonContext.Default.RegisterEndpointResponse,
            ct);
        return response.Token;
    }

    public async Task<Guid> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var response = await InvokeAsync(
            EndpointOperationId.Login,
            new LoginEndpointRequest { Request = EndpointSensitiveData.Clone(request) },
            EndpointRpcJsonContext.Default.LoginEndpointRequest,
            EndpointRpcJsonContext.Default.LoginEndpointResponse,
            ct);
        return response.Token;
    }

    public async Task<Guid> RenewAuthSessionAsync(Guid token, CancellationToken ct = default)
    {
        var response = await InvokeAsync(
            EndpointOperationId.RenewAuthSession,
            new RenewAuthSessionEndpointRequest { Token = token },
            EndpointRpcJsonContext.Default.RenewAuthSessionEndpointRequest,
            EndpointRpcJsonContext.Default.RenewAuthSessionEndpointResponse,
            ct);
        return response.RenewedToken;
    }

    public async Task LogoutAsync(Guid token, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.Logout,
            new LogoutEndpointRequest { Token = token },
            EndpointRpcJsonContext.Default.LogoutEndpointRequest,
            EndpointRpcJsonContext.Default.LogoutEndpointResponse,
            ct);
    }

    public async Task<AuthSessionStatusResponse> GetAuthSessionStatusAsync(Guid token, CancellationToken ct = default)
    {
        var response = await InvokeAsync(
            EndpointOperationId.GetAuthSessionStatus,
            new GetAuthSessionStatusEndpointRequest { Token = token },
            EndpointRpcJsonContext.Default.GetAuthSessionStatusEndpointRequest,
            EndpointRpcJsonContext.Default.GetAuthSessionStatusEndpointResponse,
            ct);
        return response.Status;
    }

    public async Task ChangeMasterPasswordAsync(MasterPasswordChangeRequest request, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.ChangeMasterPassword,
            new ChangeMasterPasswordEndpointRequest { Request = EndpointSensitiveData.Clone(request) },
            EndpointRpcJsonContext.Default.ChangeMasterPasswordEndpointRequest,
            EndpointRpcJsonContext.Default.ChangeMasterPasswordEndpointResponse,
            ct);
    }

    public async Task<UserProfileInfoResponse> GetUserProfileInfoAsync(Guid token, CancellationToken ct = default)
    {
        var response = await InvokeAsync(
            EndpointOperationId.GetUserProfileInfo,
            new GetUserProfileInfoEndpointRequest { Token = token },
            EndpointRpcJsonContext.Default.GetUserProfileInfoEndpointRequest,
            EndpointRpcJsonContext.Default.GetUserProfileInfoEndpointResponse,
            ct);
        return response.Profile;
    }

    public async Task DeleteUserAccountAsync(Guid token, byte[] password, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.DeleteUserAccount,
            new DeleteUserAccountEndpointRequest { Token = token, Password = password.ToArray() },
            EndpointRpcJsonContext.Default.DeleteUserAccountEndpointRequest,
            EndpointRpcJsonContext.Default.DeleteUserAccountEndpointResponse,
            ct);
    }

    public async Task ChangeUsernameAsync(Guid token, string newUsername, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.ChangeUsername,
            new ChangeUsernameEndpointRequest { Token = token, NewUsername = newUsername },
            EndpointRpcJsonContext.Default.ChangeUsernameEndpointRequest,
            EndpointRpcJsonContext.Default.ChangeUsernameEndpointResponse,
            ct);
    }

    public async Task UpdateUserProfileInfoAsync(UpdateUserProfileRequest request, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.UpdateUserProfileInfo,
            new UpdateUserProfileInfoEndpointRequest { Request = request },
            EndpointRpcJsonContext.Default.UpdateUserProfileInfoEndpointRequest,
            EndpointRpcJsonContext.Default.UpdateUserProfileInfoEndpointResponse,
            ct);
    }

    public async Task<LocalDeviceInfoResponse> GetLocalDeviceInfoAsync(CancellationToken ct = default)
    {
        var response = await InvokeAsync(
            EndpointOperationId.GetLocalDeviceInfo,
            new GetLocalDeviceInfoEndpointRequest(),
            EndpointRpcJsonContext.Default.GetLocalDeviceInfoEndpointRequest,
            EndpointRpcJsonContext.Default.GetLocalDeviceInfoEndpointResponse,
            ct);
        return response.Device;
    }

    public async Task<bool> GetLocalUserSyncOnAsync(Guid token, CancellationToken ct = default)
    {
        var response = await InvokeAsync(
            EndpointOperationId.GetLocalUserSyncOn,
            new GetLocalUserSyncOnEndpointRequest { Token = token },
            EndpointRpcJsonContext.Default.GetLocalUserSyncOnEndpointRequest,
            EndpointRpcJsonContext.Default.GetLocalUserSyncOnEndpointResponse,
            ct);
        return response.IsSyncOn;
    }

    public async Task SetLocalUserSyncOnAsync(Guid token, bool isSyncOn, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.SetLocalUserSyncOn,
            new SetLocalUserSyncOnEndpointRequest { Token = token, IsSyncOn = isSyncOn },
            EndpointRpcJsonContext.Default.SetLocalUserSyncOnEndpointRequest,
            EndpointRpcJsonContext.Default.SetLocalUserSyncOnEndpointResponse,
            ct);
    }

    public async Task SetLocalDeviceNameAsync(Guid token, string name, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.SetLocalDeviceName,
            new SetLocalDeviceNameEndpointRequest { Token = token, Name = name },
            EndpointRpcJsonContext.Default.SetLocalDeviceNameEndpointRequest,
            EndpointRpcJsonContext.Default.SetLocalDeviceNameEndpointResponse,
            ct);
    }

    public async Task<IReadOnlyList<UserDeviceInfoResponse>> GetUserDevicesAsync(Guid token, CancellationToken ct = default)
    {
        var response = await InvokeAsync(
            EndpointOperationId.GetUserDevices,
            new GetUserDevicesEndpointRequest { Token = token },
            EndpointRpcJsonContext.Default.GetUserDevicesEndpointRequest,
            EndpointRpcJsonContext.Default.GetUserDevicesEndpointResponse,
            ct);
        return response.Devices;
    }

    public async Task SetUserDeviceNameAsync(Guid token, Guid deviceId, string name, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.SetUserDeviceName,
            new SetUserDeviceNameEndpointRequest { Token = token, DeviceId = deviceId, Name = name },
            EndpointRpcJsonContext.Default.SetUserDeviceNameEndpointRequest,
            EndpointRpcJsonContext.Default.SetUserDeviceNameEndpointResponse,
            ct);
    }

    public async Task SetUserDeviceSyncOnAsync(Guid token, Guid deviceId, bool isSyncOn, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.SetUserDeviceSyncOn,
            new SetUserDeviceSyncOnEndpointRequest { Token = token, DeviceId = deviceId, IsSyncOn = isSyncOn },
            EndpointRpcJsonContext.Default.SetUserDeviceSyncOnEndpointRequest,
            EndpointRpcJsonContext.Default.SetUserDeviceSyncOnEndpointResponse,
            ct);
    }

    public async Task UnblockUserDeviceAsync(Guid token, Guid deviceId, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.UnblockUserDevice,
            new UnblockUserDeviceEndpointRequest { Token = token, DeviceId = deviceId },
            EndpointRpcJsonContext.Default.UnblockUserDeviceEndpointRequest,
            EndpointRpcJsonContext.Default.UnblockUserDeviceEndpointResponse,
            ct);
    }

    public async Task<DeviceRemovalResultResponse> DisconnectUserDeviceAsync(Guid token, Guid deviceId, byte[] masterPassword, CancellationToken ct = default)
    {
        var response = await InvokeAsync(
            EndpointOperationId.DisconnectUserDevice,
            new DisconnectUserDeviceEndpointRequest { Token = token, DeviceId = deviceId, MasterPassword = masterPassword.ToArray() },
            EndpointRpcJsonContext.Default.DisconnectUserDeviceEndpointRequest,
            EndpointRpcJsonContext.Default.DisconnectUserDeviceEndpointResponse,
            ct);
        return response.Result;
    }

    public async Task<DeviceEnrollmentCodeResponse> StartDeviceEnrollmentAsync(CancellationToken ct = default)
    {
        var response = await InvokeAsync(
            EndpointOperationId.StartDeviceEnrollment,
            new StartDeviceEnrollmentEndpointRequest(),
            EndpointRpcJsonContext.Default.StartDeviceEnrollmentEndpointRequest,
            EndpointRpcJsonContext.Default.StartDeviceEnrollmentEndpointResponse,
            ct);
        return response.Enrollment;
    }

    public async Task<DeviceEnrollmentStatusResponse> GetDeviceEnrollmentStatusAsync(CancellationToken ct = default)
    {
        var response = await InvokeAsync(
            EndpointOperationId.GetDeviceEnrollmentStatus,
            new GetDeviceEnrollmentStatusEndpointRequest(),
            EndpointRpcJsonContext.Default.GetDeviceEnrollmentStatusEndpointRequest,
            EndpointRpcJsonContext.Default.GetDeviceEnrollmentStatusEndpointResponse,
            ct);
        return response.Status;
    }

    public async Task CancelDeviceEnrollmentAsync(CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.CancelDeviceEnrollment,
            new CancelDeviceEnrollmentEndpointRequest(),
            EndpointRpcJsonContext.Default.CancelDeviceEnrollmentEndpointRequest,
            EndpointRpcJsonContext.Default.CancelDeviceEnrollmentEndpointResponse,
            ct);
    }

    public async Task AddDeviceByCodeAsync(Guid token, string code, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.AddDeviceByCode,
            new AddDeviceByCodeEndpointRequest { Token = token, Code = code },
            EndpointRpcJsonContext.Default.AddDeviceByCodeEndpointRequest,
            EndpointRpcJsonContext.Default.AddDeviceByCodeEndpointResponse,
            ct);
    }

    public async Task<IReadOnlyList<Guid>> RestoreRememberedSessionsAsync(CancellationToken ct = default)
    {
        var response = await InvokeAsync(
            EndpointOperationId.RestoreRememberedSessions,
            new RestoreRememberedSessionsEndpointRequest(),
            EndpointRpcJsonContext.Default.RestoreRememberedSessionsEndpointRequest,
            EndpointRpcJsonContext.Default.RestoreRememberedSessionsEndpointResponse,
            ct);
        return response.Tokens;
    }

    public async Task<Guid> InitializeRememberMeSessionAsync(Guid userId, CancellationToken ct = default)
    {
        var response = await InvokeAsync(
            EndpointOperationId.InitializeRememberMeSession,
            new InitializeRememberMeSessionEndpointRequest { UserId = userId },
            EndpointRpcJsonContext.Default.InitializeRememberMeSessionEndpointRequest,
            EndpointRpcJsonContext.Default.InitializeRememberMeSessionEndpointResponse,
            ct);
        return response.Token;
    }

    public async Task SetRememberMeAsync(Guid token, bool rememberMe, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.SetRememberMe,
            new SetRememberMeEndpointRequest { Token = token, RememberMe = rememberMe },
            EndpointRpcJsonContext.Default.SetRememberMeEndpointRequest,
            EndpointRpcJsonContext.Default.SetRememberMeEndpointResponse,
            ct);
    }

    public async Task<SavedPasswordsResponse> GetSavedPasswordsAsync(Guid token, CancellationToken ct = default)
    {
        var response = await InvokeAsync(
            EndpointOperationId.GetSavedPasswords,
            new GetSavedPasswordsEndpointRequest { Token = token },
            EndpointRpcJsonContext.Default.GetSavedPasswordsEndpointRequest,
            EndpointRpcJsonContext.Default.GetSavedPasswordsEndpointResponse,
            ct);
        return response.Passwords;
    }

    public async Task AddNewPasswordAsync(Guid token, NewPasswordRequest request, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.AddNewPassword,
            new AddNewPasswordEndpointRequest { Token = token, Request = EndpointSensitiveData.Clone(request) },
            EndpointRpcJsonContext.Default.AddNewPasswordEndpointRequest,
            EndpointRpcJsonContext.Default.AddNewPasswordEndpointResponse,
            ct);
    }

    public async Task RemovePasswordsAsync(Guid token, IReadOnlyList<Guid> passwordIds, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.RemovePasswords,
            new RemovePasswordsEndpointRequest { Token = token, PasswordIds = passwordIds.ToArray() },
            EndpointRpcJsonContext.Default.RemovePasswordsEndpointRequest,
            EndpointRpcJsonContext.Default.RemovePasswordsEndpointResponse,
            ct);
    }

    public async Task<byte[]> GetUnsecurePasswordAsync(Guid token, Guid passwordId, CancellationToken ct = default)
    {
        var response = await InvokeAsync(
            EndpointOperationId.GetUnsecurePassword,
            new GetUnsecurePasswordEndpointRequest { Token = token, PasswordId = passwordId },
            EndpointRpcJsonContext.Default.GetUnsecurePasswordEndpointRequest,
            EndpointRpcJsonContext.Default.GetUnsecurePasswordEndpointResponse,
            ct);
        return response.Password;
    }

    public async Task UpdatePasswordAsync(Guid token, UpdatePasswordRequest request, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.UpdatePassword,
            new UpdatePasswordEndpointRequest { Token = token, Request = EndpointSensitiveData.Clone(request) },
            EndpointRpcJsonContext.Default.UpdatePasswordEndpointRequest,
            EndpointRpcJsonContext.Default.UpdatePasswordEndpointResponse,
            ct);
    }

    public async Task ExportPasswordsToUserAsync(Guid sourceToken, ExportPasswordsToUserRequest request, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.ExportPasswordsToUser,
            new ExportPasswordsToUserEndpointRequest { SourceToken = sourceToken, Request = request },
            EndpointRpcJsonContext.Default.ExportPasswordsToUserEndpointRequest,
            EndpointRpcJsonContext.Default.ExportPasswordsToUserEndpointResponse,
            ct);
    }

    public async Task AddCustomUserColorsAsync(Guid token, IReadOnlyList<NewCustomUserColorRequest> requests, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.AddCustomUserColors,
            new AddCustomUserColorsEndpointRequest { Token = token, Requests = requests.ToArray() },
            EndpointRpcJsonContext.Default.AddCustomUserColorsEndpointRequest,
            EndpointRpcJsonContext.Default.AddCustomUserColorsEndpointResponse,
            ct);
    }

    public async Task DeleteCustomUserColorsAsync(Guid token, IReadOnlyList<Guid> customUserColorIds, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.DeleteCustomUserColors,
            new DeleteCustomUserColorsEndpointRequest { Token = token, CustomUserColorIds = customUserColorIds.ToArray() },
            EndpointRpcJsonContext.Default.DeleteCustomUserColorsEndpointRequest,
            EndpointRpcJsonContext.Default.DeleteCustomUserColorsEndpointResponse,
            ct);
    }

    public async Task ExportCustomUserColorsToUserAsync(Guid sourceToken, ExportCustomUserColorsToUserRequest request, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.ExportCustomUserColorsToUser,
            new ExportCustomUserColorsToUserEndpointRequest { SourceToken = sourceToken, Request = request },
            EndpointRpcJsonContext.Default.ExportCustomUserColorsToUserEndpointRequest,
            EndpointRpcJsonContext.Default.ExportCustomUserColorsToUserEndpointResponse,
            ct);
    }

    public async Task UpdateCustomUserColorAsync(Guid token, UpdateCustomUserColorRequest request, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.UpdateCustomUserColor,
            new UpdateCustomUserColorEndpointRequest { Token = token, Request = request },
            EndpointRpcJsonContext.Default.UpdateCustomUserColorEndpointRequest,
            EndpointRpcJsonContext.Default.UpdateCustomUserColorEndpointResponse,
            ct);
    }

    public async Task AddPasswordTagAsync(Guid token, NewPasswordTagRequest request, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.AddPasswordTag,
            new AddPasswordTagEndpointRequest { Token = token, Request = request },
            EndpointRpcJsonContext.Default.AddPasswordTagEndpointRequest,
            EndpointRpcJsonContext.Default.AddPasswordTagEndpointResponse,
            ct);
    }

    public async Task DeletePasswordTagAsync(Guid token, Guid passwordTagId, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.DeletePasswordTag,
            new DeletePasswordTagEndpointRequest { Token = token, PasswordTagId = passwordTagId },
            EndpointRpcJsonContext.Default.DeletePasswordTagEndpointRequest,
            EndpointRpcJsonContext.Default.DeletePasswordTagEndpointResponse,
            ct);
    }

    public async Task ExportPasswordTagsToUserAsync(Guid sourceToken, ExportPasswordTagsToUserRequest request, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.ExportPasswordTagsToUser,
            new ExportPasswordTagsToUserEndpointRequest { SourceToken = sourceToken, Request = request },
            EndpointRpcJsonContext.Default.ExportPasswordTagsToUserEndpointRequest,
            EndpointRpcJsonContext.Default.ExportPasswordTagsToUserEndpointResponse,
            ct);
    }

    public async Task UpdatePasswordTagAsync(Guid token, UpdatePasswordTagRequest request, CancellationToken ct = default)
    {
        await InvokeAsync(
            EndpointOperationId.UpdatePasswordTag,
            new UpdatePasswordTagEndpointRequest { Token = token, Request = request },
            EndpointRpcJsonContext.Default.UpdatePasswordTagEndpointRequest,
            EndpointRpcJsonContext.Default.UpdatePasswordTagEndpointResponse,
            ct);
    }

    private async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        EndpointOperationId operationId,
        TRequest request,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class
    {
        var descriptor = EndpointOperationManifest.Get(operationId);
        byte[]? requestPayload = null;
        byte[]? responsePayload = null;
        TResponse? response = null;
        var transmissionAttempted = false;
        var responseTransferred = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _validator.ValidateRequest(operationId, request);
            requestPayload = _serializer.Serialize(request, requestTypeInfo);
            if (requestPayload.Length > descriptor.MaximumRequestPayloadSize)
                throw new EndpointRpcPayloadException("The endpoint RPC request payload exceeds the permitted size.");

            transmissionAttempted = true;
            responsePayload = await _transport.SendAsync(
                operationId,
                requestPayload,
                descriptor.CancellationClassification,
                cancellationToken);
            if (responsePayload.Length > descriptor.MaximumResponsePayloadSize)
                throw new EndpointRpcPayloadException("The endpoint RPC response payload exceeds the permitted size.");

            response = _serializer.Deserialize(responsePayload, responseTypeInfo);
            _validator.ValidateResponse(operationId, response);
            responseTransferred = true;
            return response;
        }
        catch (EndpointRpcRemoteException exception)
        {
            throw MapRemoteFailure(operationId, descriptor.CancellationClassification, exception);
        }
        catch (OperationCanceledException exception)
            when (transmissionAttempted &&
                descriptor.CancellationClassification != EndpointOperationCancellationClassification.ReadOnlySafelyCancellable)
        {
            throw new EndpointOperationOutcomeUnknownException(operationId, exception);
        }
        catch (EndpointRpcDisconnectedException exception)
            when (transmissionAttempted &&
                descriptor.CancellationClassification != EndpointOperationCancellationClassification.ReadOnlySafelyCancellable)
        {
            throw new EndpointOperationOutcomeUnknownException(operationId, exception);
        }
        catch (EndpointRpcPayloadException exception)
            when (transmissionAttempted &&
                descriptor.CancellationClassification != EndpointOperationCancellationClassification.ReadOnlySafelyCancellable)
        {
            throw new EndpointOperationOutcomeUnknownException(operationId, exception);
        }
        finally
        {
            if (!responseTransferred && response is not null)
                EndpointSensitiveData.ClearResponse(operationId, response);
            EndpointSensitiveData.ClearRequest(operationId, request);
            EndpointSensitiveData.Clear(requestPayload);
            EndpointSensitiveData.Clear(responsePayload);
        }
    }

    private static Exception MapRemoteFailure(
        EndpointOperationId operationId,
        EndpointOperationCancellationClassification cancellationClassification,
        EndpointRpcRemoteException exception)
    {
        if (exception.Error.ErrorCode == EndpointRpcErrorCode.OperationCancelled)
        {
            return cancellationClassification == EndpointOperationCancellationClassification.ReadOnlySafelyCancellable
                ? new OperationCanceledException(exception.Error.SafeMessage, exception)
                : new EndpointOperationOutcomeUnknownException(operationId, exception);
        }

        return exception.Error.ErrorCode switch
        {
            EndpointRpcErrorCode.OperationOutcomeUnknown =>
                new EndpointOperationOutcomeUnknownException(operationId, exception),
            EndpointRpcErrorCode.Disconnected or EndpointRpcErrorCode.RuntimeUnavailable
                when cancellationClassification != EndpointOperationCancellationClassification.ReadOnlySafelyCancellable =>
                new EndpointOperationOutcomeUnknownException(operationId, exception),
            EndpointRpcErrorCode.ResponsePayloadTooLarge
                when cancellationClassification != EndpointOperationCancellationClassification.ReadOnlySafelyCancellable =>
                new EndpointOperationOutcomeUnknownException(operationId, exception),
            EndpointRpcErrorCode.Disconnected => new EndpointRpcDisconnectedException(exception),
            _ => exception
        };
    }
}
