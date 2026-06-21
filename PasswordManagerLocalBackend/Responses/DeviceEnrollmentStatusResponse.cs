using PasswordManagerLocalBackend.Exceptions;

namespace PasswordManagerLocalBackend.Responses;

public sealed class DeviceEnrollmentStatusResponse
{
    public DeviceEnrollmentState State { get; set; }
    public DeviceEnrollmentErrorCode ErrorCode { get; set; } = DeviceEnrollmentErrorCode.Unknown;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    public bool IsCompleted => State == DeviceEnrollmentState.Completed;
    public bool IsFinished => State == DeviceEnrollmentState.Completed || State == DeviceEnrollmentState.Failed || State == DeviceEnrollmentState.Expired;
}
