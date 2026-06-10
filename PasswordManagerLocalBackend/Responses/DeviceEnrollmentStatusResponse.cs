namespace PasswordManagerLocalBackend.Responses;

public enum DeviceEnrollmentState
{
    None,
    Waiting,
    Completed,
    Failed,
    Expired
}

public sealed class DeviceEnrollmentStatusResponse
{
    public DeviceEnrollmentState State { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    public bool IsCompleted => State == DeviceEnrollmentState.Completed;
    public bool IsFinished => State == DeviceEnrollmentState.Completed || State == DeviceEnrollmentState.Failed || State == DeviceEnrollmentState.Expired;
}
