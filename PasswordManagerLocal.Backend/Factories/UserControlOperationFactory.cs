using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Factories;

internal static class UserControlOperationFactory
{
    internal static UserControlOperation Create(UserControlOperationEnvelope envelope, byte[] serialized, UserControlOperationStatus status, Guid? lastReceivedFromDeviceId = null) =>
        new()
        {
            OperationId = envelope.OperationId,
            UserId = envelope.UserId,
            OperationType = envelope.OperationType,
            OriginDeviceId = envelope.OriginDeviceId,
            OriginInstanceId = envelope.OriginInstanceId,
            OriginSequence = envelope.OriginSequence,
            PreviousKeyEpoch = envelope.PreviousKeyEpoch,
            ResultingKeyEpoch = envelope.ResultingKeyEpoch,
            PreviousMembershipEpoch = envelope.PreviousMembershipEpoch,
            ResultingMembershipEpoch = envelope.ResultingMembershipEpoch,
            CreatedAtUtc = envelope.CreatedAtUtc,
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            AppliedAtUtc = status == UserControlOperationStatus.Applied ? DateTimeOffset.UtcNow : null,
            LastReceivedFromDeviceId = lastReceivedFromDeviceId,
            PayloadHash = envelope.PayloadHash.ToArray(),
            OperationHash = envelope.OperationHash.ToArray(),
            OriginSignPublicKey = envelope.OriginSignPublicKey.ToArray(),
            OriginSignature = envelope.OriginSignature.ToArray(),
            EnvelopePayload = serialized,
            Status = status
        };
}
