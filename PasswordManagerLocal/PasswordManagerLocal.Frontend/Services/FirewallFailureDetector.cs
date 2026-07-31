using PasswordManagerLocal.Contracts.Errors;
using System.Net.Sockets;

using PasswordManagerLocal.Contracts.Enrollment;

namespace PasswordManagerLocal.Frontend.Services;

public static class FirewallFailureDetector
{
    public static bool IsLikelyFirewallRelated(Exception exception)
    {
        if (exception is AggregateException aggregateException)
            return aggregateException.Flatten().InnerExceptions.Any(IsLikelyFirewallRelated);

        if (exception is DeviceEnrollmentException enrollmentException &&
            enrollmentException.ErrorCode is
                DeviceEnrollmentErrorCode.NewDeviceNotFound or
                DeviceEnrollmentErrorCode.NewDeviceConnectionFailed or
                DeviceEnrollmentErrorCode.LocalNetworkUnavailable or
                DeviceEnrollmentErrorCode.LocalEnrollmentListenerUnavailable)
        {
            return true;
        }

        if (exception is SocketException or TimeoutException)
            return true;

        return exception.InnerException is not null &&
            IsLikelyFirewallRelated(exception.InnerException);
    }
}
