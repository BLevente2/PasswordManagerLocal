using PasswordManagerLocal.Contracts.Enrollment;

namespace PasswordManagerLocal.Contracts.Errors;

public sealed class DeviceEnrollmentException : Exception
{
    public DeviceEnrollmentException(string message)
        : this(DeviceEnrollmentErrorCode.Unknown, message, isKnownNotCommitted: false)
    {
    }

    public DeviceEnrollmentException(string message, Exception innerException)
        : this(DeviceEnrollmentErrorCode.Unknown, message, isKnownNotCommitted: false, innerException)
    {
    }

    public DeviceEnrollmentException(DeviceEnrollmentErrorCode errorCode, string message)
        : this(errorCode, message, errorCode != DeviceEnrollmentErrorCode.Unknown)
    {
    }

    public DeviceEnrollmentException(
        DeviceEnrollmentErrorCode errorCode,
        string message,
        Exception innerException)
        : this(errorCode, message, errorCode != DeviceEnrollmentErrorCode.Unknown, innerException)
    {
    }

    public DeviceEnrollmentException(
        DeviceEnrollmentErrorCode errorCode,
        string message,
        bool isKnownNotCommitted,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        IsKnownNotCommitted = isKnownNotCommitted;
    }

    public DeviceEnrollmentErrorCode ErrorCode { get; }
    public bool IsKnownNotCommitted { get; }
}
