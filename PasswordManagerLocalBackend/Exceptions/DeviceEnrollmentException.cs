namespace PasswordManagerLocalBackend.Exceptions;

public enum DeviceEnrollmentErrorCode
{
    Unknown,
    SyncDisabled,
    InvalidCode,
    NewDeviceNotFound,
    NewDeviceConnectionFailed,
    NewDeviceRejected,
    CodeExpired,
    CodeProofInvalid,
    ProfileDataInvalid,
    ProfileDataTooLarge,
    DeviceIdentityConflict
}

public sealed class DeviceEnrollmentException : Exception
{
    public DeviceEnrollmentErrorCode ErrorCode { get; }

    public DeviceEnrollmentException(string message) : this(DeviceEnrollmentErrorCode.Unknown, message)
    {
    }


    public DeviceEnrollmentException(string message, Exception innerException) : this(DeviceEnrollmentErrorCode.Unknown, message, innerException)
    {
    }


    public DeviceEnrollmentException(DeviceEnrollmentErrorCode errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }


    public DeviceEnrollmentException(DeviceEnrollmentErrorCode errorCode, string message, Exception innerException) : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
