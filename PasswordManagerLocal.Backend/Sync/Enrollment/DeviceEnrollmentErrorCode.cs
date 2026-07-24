namespace PasswordManagerLocal.Backend.Sync.Enrollment;

public enum DeviceEnrollmentErrorCode
{
    Unknown,
    SyncDisabled,
    InteractiveSessionRequired,
    InvalidCode,
    NewDeviceNotFound,
    NewDeviceConnectionFailed,
    NewDeviceRejected,
    CodeExpired,
    CodeProofInvalid,
    ProfileDataInvalid,
    ProfileDataTooLarge,
    DeviceIdentityConflict,
    UnsupportedDatabaseVersion,
    LocalNetworkUnavailable,
    LocalEnrollmentListenerUnavailable
}
