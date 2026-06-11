namespace PasswordManagerLocalBackend.Constants;

public static class SyncConstants
{
    public const string PFXPassword = "";

    public const int SyncPort = 26688;
    public const string MdnsServiceType = "_pmlsync._tcp";
    public const int MdnsThrottleSeconds = 20;
    public const int MdnsResolveTimeoutSeconds = 2;
    public const int DeviceEnrollmentDiscoveryTimeoutSeconds = 30;
    public const int DeviceEnrollmentConnectTimeoutSeconds = 4;
    public const int DeviceEnrollmentTransferTimeoutSeconds = 120;

    public const int MaxIncomingDeltaPayloadBytes = 4 * 1024 * 1024;
    public const int MaxDeviceEnrollmentSnapshotBytes = 64 * 1024 * 1024;
    public const int DeviceEnrollmentSnapshotChunkBytes = 64 * 1024;
    public const int SyncDeltaEncryptionVersion = 1;
    public const int SyncDeltaNonceBytes = 12;
    public const int SyncDeltaTagBytes = 16;
    public const int SyncDeltaPayloadHashBytes = 64;
    public const int SyncDeltaX25519PublicKeyBytes = 32;
    public const int SyncDeltaEd25519PublicKeyBytes = 32;
    public const int SyncDeltaEd25519SignatureBytes = 64;
    public const int MaxIncomingDeltaCountPerCall = 256;
    public const int MaxIncomingDeltaFutureSeconds = 300;
    public const int MaxInvalidIncomingSyncAttempts = 5;
}