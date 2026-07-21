namespace PasswordManagerLocal.Windows.Ipc.Contracts;

public enum IpcErrorCode
{
    ProtocolViolation = 1,
    UnsupportedProtocolVersion = 2,
    HandshakeRequired = 3,
    DuplicateHandshake = 4,
    UnexpectedPeerRole = 5,
    UnknownOperation = 6,
    InvalidEnvelope = 7,
    InvalidPayload = 8,
    HandlerFailed = 9,
    RequestCancelled = 10,
    ConnectionClosed = 11,
    UiAlreadyRegistered = 12,
    RequestRejected = 13,
    InternalFailure = 14,
    ClientRequestLimitReached = 15,
    ServerBusy = 16,
    TooManyRequests = 17,
    RequestPayloadTooLarge = 18,
    ResponsePayloadTooLarge = 19,
    SerializedEnvelopeTooLarge = 20
}
