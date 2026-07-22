using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Sync.Enrollment;
using PasswordManagerLocal.Windows.EndpointRpc.Contracts;
using System.Security.Cryptography;

namespace PasswordManagerLocal.Windows.EndpointRpc.Server;

public sealed class EndpointRpcBackendErrorMapper
{
    public EndpointRpcError Map(Exception exception, EndpointRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        var metadata = exception switch
        {
            ArgumentException =>
                Error(EndpointRpcErrorCode.ValidationFailed, EndpointRpcErrorCategory.Validation, "The endpoint input is invalid."),
            SyncRouteDisabledException =>
                Error(EndpointRpcErrorCode.OperationRejected, EndpointRpcErrorCategory.Validation, "The endpoint operation was rejected."),
            UserNotFoundException or UsernameChangedDuringLoginException
                when context.OperationId == EndpointOperationId.Login =>
                Error(EndpointRpcErrorCode.AuthenticationFailed, EndpointRpcErrorCategory.Authentication, "Authentication failed."),
            UnauthorizedAccessException when context.OperationId == EndpointOperationId.Login =>
                Error(EndpointRpcErrorCode.AuthenticationFailed, EndpointRpcErrorCategory.Authentication, "Authentication failed."),
            InvalidTokenException =>
                Error(EndpointRpcErrorCode.AuthorizationFailed, EndpointRpcErrorCategory.Authorization, "The endpoint authorization is invalid."),
            UnauthorizedAccessException =>
                Error(EndpointRpcErrorCode.AuthorizationFailed, EndpointRpcErrorCategory.Authorization, "The endpoint operation is not authorized."),
            UserNotFoundException or PasswordNotFoundException or PasswordTagNotFoundException or CustomUserColorNotFoundException =>
                Error(EndpointRpcErrorCode.NotFound, EndpointRpcErrorCategory.NotFound, "The requested endpoint resource was not found."),
            DuplicatePasswordNameException or DuplicatePasswordTagNameException or
                DuplicateCustomUserColorNameException or DuplicateCustomUserColorCodeException or
                DeterministicSyncConflictException or RecoveryEvidenceConflictException =>
                Error(EndpointRpcErrorCode.Conflict, EndpointRpcErrorCategory.Conflict, "The endpoint operation conflicts with current state."),
            LimitReachedException =>
                Error(EndpointRpcErrorCode.OperationRejected, EndpointRpcErrorCategory.Validation, "The endpoint operation was rejected."),
            DeviceEnrollmentException enrollmentException =>
                MapDeviceEnrollmentError(enrollmentException.ErrorCode),
            DeviceIdentityNotInitilaizedException =>
                Error(EndpointRpcErrorCode.RuntimeUnavailable, EndpointRpcErrorCategory.Availability, "The backend runtime is temporarily unavailable.", true),
            KeyProtectorUnavailableException =>
                Error(EndpointRpcErrorCode.RuntimeUnavailable, EndpointRpcErrorCategory.Availability, "The backend runtime is temporarily unavailable.", true),
            DatabaseVersionNotSupportedException =>
                Error(EndpointRpcErrorCode.RuntimeUnavailable, EndpointRpcErrorCategory.Availability, "The backend runtime requires process replacement.", false, true),
            CryptographicException or InvalidDataIntegrityException =>
                Error(EndpointRpcErrorCode.BackendFailure, EndpointRpcErrorCategory.Internal, "The backend could not safely complete the operation."),
            InvalidOperationException =>
                Error(EndpointRpcErrorCode.InteractiveSessionUnavailable, EndpointRpcErrorCategory.Availability, "The interactive backend session is unavailable."),
            _ => Error(EndpointRpcErrorCode.BackendFailure, EndpointRpcErrorCategory.Internal, "The endpoint operation failed.")
        };

        return new EndpointRpcError(
            metadata.Code,
            metadata.Category,
            metadata.Message,
            context.CorrelationId,
            DateTimeOffset.UtcNow,
            metadata.Retryable,
            metadata.RequiresRestart);
    }



    public bool IsConclusiveMutationFailure(
        Exception exception,
        EndpointRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        return exception switch
        {
            InvalidInputException => true,
            UserNotFoundException or UsernameChangedDuringLoginException
                when context.OperationId == EndpointOperationId.Login => true,
            UnauthorizedAccessException when context.OperationId == EndpointOperationId.Login => true,
            InvalidTokenException => true,
            UserNotFoundException or PasswordNotFoundException or PasswordTagNotFoundException or
                CustomUserColorNotFoundException => true,
            DuplicatePasswordNameException or DuplicatePasswordTagNameException or
                DuplicateCustomUserColorNameException or DuplicateCustomUserColorCodeException => true,
            LimitReachedException => true,
            DeviceEnrollmentException enrollmentException => enrollmentException.ErrorCode is
                DeviceEnrollmentErrorCode.NewDeviceNotFound or
                DeviceEnrollmentErrorCode.DeviceIdentityConflict,
            _ => false
        };
    }

    public EndpointRpcError CreateOutcomeUnknown(EndpointRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new EndpointRpcError(
            EndpointRpcErrorCode.OperationOutcomeUnknown,
            EndpointRpcErrorCategory.Internal,
            "The endpoint operation may have executed, but its outcome could not be confirmed.",
            context.CorrelationId,
            DateTimeOffset.UtcNow,
            IsRetryable: false,
            RequiresProcessRestart: false);
    }

    private static (EndpointRpcErrorCode Code, EndpointRpcErrorCategory Category, string Message, bool Retryable, bool RequiresRestart) MapDeviceEnrollmentError(
        DeviceEnrollmentErrorCode errorCode) => errorCode switch
    {
        DeviceEnrollmentErrorCode.NewDeviceNotFound =>
            Error(EndpointRpcErrorCode.NotFound, EndpointRpcErrorCategory.NotFound, "The requested endpoint resource was not found."),
        DeviceEnrollmentErrorCode.DeviceIdentityConflict =>
            Error(EndpointRpcErrorCode.Conflict, EndpointRpcErrorCategory.Conflict, "The endpoint operation conflicts with current state."),
        DeviceEnrollmentErrorCode.UnsupportedDatabaseVersion =>
            Error(EndpointRpcErrorCode.RuntimeUnavailable, EndpointRpcErrorCategory.Availability, "The backend runtime requires process replacement.", false, true),
        DeviceEnrollmentErrorCode.NewDeviceConnectionFailed or
            DeviceEnrollmentErrorCode.LocalNetworkUnavailable or
            DeviceEnrollmentErrorCode.LocalEnrollmentListenerUnavailable =>
            Error(EndpointRpcErrorCode.RuntimeUnavailable, EndpointRpcErrorCategory.Availability, "The backend runtime is temporarily unavailable.", true),
        _ =>
            Error(EndpointRpcErrorCode.OperationRejected, EndpointRpcErrorCategory.Validation, "The endpoint operation was rejected.")
    };

    private static (EndpointRpcErrorCode Code, EndpointRpcErrorCategory Category, string Message, bool Retryable, bool RequiresRestart) Error(
        EndpointRpcErrorCode code,
        EndpointRpcErrorCategory category,
        string message,
        bool retryable = false,
        bool requiresRestart = false) =>
        (code, category, message, retryable, requiresRestart);
}
