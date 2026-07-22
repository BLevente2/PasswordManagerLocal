using PasswordManagerLocal.Windows.EndpointRpc.Contracts;

namespace PasswordManagerLocal.Windows.EndpointRpc.Metadata;

public sealed record EndpointOperationDescriptor(
    EndpointOperationId OperationId,
    string MethodName,
    Type RequestType,
    Type ResponseType,
    EndpointOperationCancellationClassification CancellationClassification,
    bool MutatesState,
    bool IsIdempotent,
    bool HandlesSensitiveData,
    int MaximumRequestPayloadSize,
    int MaximumResponsePayloadSize);
