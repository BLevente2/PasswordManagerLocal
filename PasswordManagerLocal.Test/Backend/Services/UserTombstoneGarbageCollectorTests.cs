using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.Backend.Services;

[TestClass]
public sealed class UserTombstoneGarbageCollectorTests
{
    [TestMethod]
    [TestCategory("Backend")]
    public void Evaluate_ActiveDeviceWithoutMergedReceipt_IsUnstable()
    {
        var fixture = Fixture.Create();
        var receipts = fixture.ReceiptsFor(fixture.Origin);

        var result = fixture.Evaluate(receipts: receipts);

        MSTestAssert.AreEqual(TombstoneGarbageCollectionReason.MissingMergedReceipt, result.Reason);
        MSTestAssert.AreEqual(fixture.Peer.DeviceId, result.BlockingDeviceId);
    }

    [TestMethod]
    [TestCategory("Backend")]
    public void Evaluate_StoredOnlyAnchorKnowledge_IsInsufficient()
    {
        var fixture = Fixture.Create();
        fixture.Knowledge[fixture.AnchorKey] = Knowledge(fixture.Reference, stored: 7, merged: 6);

        var result = fixture.Evaluate(receipts: fixture.ReceiptsFor(fixture.Origin, fixture.Peer));

        MSTestAssert.AreEqual(TombstoneGarbageCollectionReason.StoredOnlyKnowledge, result.Reason);
    }

    [TestMethod]
    [TestCategory("Backend")]
    public void Evaluate_AllRelevantDevicesCoverDeletion_IsStable()
    {
        var fixture = Fixture.Create();

        var result = fixture.Evaluate(receipts: fixture.ReceiptsFor(fixture.Origin, fixture.Peer));

        MSTestAssert.AreEqual(TombstoneGarbageCollectionReason.Stable, result.Reason);
    }

    [TestMethod]
    [TestCategory("Backend")]
    public void Evaluate_DeviceRemovedBeforeDeletion_DoesNotBlock()
    {
        var fixture = Fixture.Create();
        var removedBefore = Authorization(Guid.NewGuid(), Guid.NewGuid(), started: 1, ended: 2, active: false);
        removedBefore.UserId = fixture.UserId;
        fixture.Authorizations.Add(removedBefore);

        var result = fixture.Evaluate(receipts: fixture.ReceiptsFor(fixture.Origin, fixture.Peer));

        MSTestAssert.AreEqual(TombstoneGarbageCollectionReason.Stable, result.Reason);
    }

    [TestMethod]
    [TestCategory("Backend")]
    public void Evaluate_DeviceRemovedAfterDeletion_WithMergedCutoff_DoesNotBlock()
    {
        var fixture = Fixture.Create(includePeer: false);
        var removed = Authorization(Guid.NewGuid(), Guid.NewGuid(), started: 1, ended: 3, active: false);
        fixture.Authorizations.Add(removed);
        fixture.AddRemovalEvidence(removed, acceptedRevision: 5, mergedRevision: 5);

        var result = fixture.Evaluate(receipts: fixture.ReceiptsFor(fixture.Origin));

        MSTestAssert.AreEqual(TombstoneGarbageCollectionReason.Stable, result.Reason);
    }

    [TestMethod]
    [TestCategory("Backend")]
    public void Evaluate_DeviceRemovedAfterDeletion_WithZeroAcceptedRevision_DoesNotRequireKnowledgeRow()
    {
        var fixture = Fixture.Create(includePeer: false);
        var removed = Authorization(Guid.NewGuid(), Guid.NewGuid(), started: 1, ended: 3, active: false);
        fixture.Authorizations.Add(removed);
        fixture.AddRemovalEvidence(removed, acceptedRevision: 0, mergedRevision: null);

        var result = fixture.Evaluate(receipts: fixture.ReceiptsFor(fixture.Origin));

        MSTestAssert.AreEqual(TombstoneGarbageCollectionReason.Stable, result.Reason);
    }

    [TestMethod]
    [TestCategory("Backend")]
    public void Evaluate_DeviceRemovedAfterDeletion_WithUnmergedAcceptedRevision_Blocks()
    {
        var fixture = Fixture.Create(includePeer: false);
        var removed = Authorization(Guid.NewGuid(), Guid.NewGuid(), started: 1, ended: 3, active: false);
        fixture.Authorizations.Add(removed);
        fixture.AddRemovalEvidence(removed, acceptedRevision: 5, mergedRevision: 4);

        var result = fixture.Evaluate(receipts: fixture.ReceiptsFor(fixture.Origin));

        MSTestAssert.AreEqual(TombstoneGarbageCollectionReason.RemovalCutoffNotMerged, result.Reason);
        MSTestAssert.AreEqual(removed.DeviceId, result.BlockingDeviceId);
    }

    [TestMethod]
    [TestCategory("Backend")]
    public void Evaluate_DeviceEnrolledAfterDeletion_DoesNotBecomeHistoricalBlocker()
    {
        var fixture = Fixture.Create();
        var addedAfter = Authorization(Guid.NewGuid(), Guid.NewGuid(), started: 3, ended: null, active: true);
        addedAfter.UserId = fixture.UserId;
        fixture.Authorizations.Add(addedAfter);

        var result = fixture.Evaluate(receipts: fixture.ReceiptsFor(fixture.Origin, fixture.Peer));

        MSTestAssert.AreEqual(TombstoneGarbageCollectionReason.Stable, result.Reason);
    }

    [TestMethod]
    [TestCategory("Backend")]
    public void Evaluate_CurrentEpochReplacementReceipt_CanCoverPriorEpochDeletion()
    {
        var fixture = Fixture.Create();
        var receipts = fixture.ReceiptsForKeyEpoch(2, fixture.Origin, fixture.Peer);

        var result = fixture.Evaluate(receipts: receipts);

        MSTestAssert.AreEqual(TombstoneGarbageCollectionReason.Stable, result.Reason);
    }

    [TestMethod]
    [TestCategory("Backend")]
    public void Evaluate_UnknownDeletionOriginMembership_Blocks()
    {
        var fixture = Fixture.Create(includeOrigin: false);

        var result = fixture.Evaluate(receipts: fixture.ReceiptsFor(fixture.Peer));

        MSTestAssert.AreEqual(TombstoneGarbageCollectionReason.UnknownHistoricalMembership, result.Reason);
    }

    [TestMethod]
    [TestCategory("Backend")]
    public void Evaluate_DifferentEnumerationOrders_ProduceSameDecision()
    {
        var fixture = Fixture.Create();
        var first = fixture.Evaluate(
            authorizations: fixture.Authorizations.ToArray(),
            receipts: fixture.ReceiptsFor(fixture.Origin, fixture.Peer));
        var reversedReceipts = fixture.ReceiptsFor(fixture.Peer, fixture.Origin)
            .ToDictionary(item => item.Key, item => item.Value.AsEnumerable().Reverse().ToList());
        var second = fixture.Evaluate(
            authorizations: fixture.Authorizations.AsEnumerable().Reverse().ToArray(),
            receipts: reversedReceipts);

        MSTestAssert.AreEqual(first, second);
        MSTestAssert.AreEqual(TombstoneGarbageCollectionReason.Stable, second.Reason);
    }

    [TestMethod]
    [TestCategory("Backend")]
    public void SortRemainingTombstones_UsesDeterministicItemIdOrder()
    {
        var low = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var high = Guid.Parse("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF");
        using var bundle = new UserDataBundle();
        bundle.UserPasswordsData.DeletedPasswords.AddRange(
        [
            new DeletedPasswordData { Id = high },
            new DeletedPasswordData { Id = low }
        ]);
        bundle.UserPasswordsData.DeletedTags.AddRange(
        [
            new DeletedPasswordTagData { Id = high },
            new DeletedPasswordTagData { Id = low }
        ]);
        bundle.UserPasswordsData.DeletedCustomColors.AddRange(
        [
            new DeletedCustomUserColorData { Id = high },
            new DeletedCustomUserColorData { Id = low }
        ]);
        bundle.UserDevicesData.DeletedDevices.AddRange(
        [
            new DeletedUserDeviceData { Id = high },
            new DeletedUserDeviceData { Id = low }
        ]);

        UserTombstoneGarbageCollector.SortRemainingTombstones(bundle);

        MSTestAssert.AreEqual(low, bundle.UserPasswordsData.DeletedPasswords[0].Id);
        MSTestAssert.AreEqual(low, bundle.UserPasswordsData.DeletedTags[0].Id);
        MSTestAssert.AreEqual(low, bundle.UserPasswordsData.DeletedCustomColors[0].Id);
        MSTestAssert.AreEqual(low, bundle.UserDevicesData.DeletedDevices[0].Id);
    }

    [TestMethod]
    [TestCategory("Backend")]
    public void Covers_StoredSnapshotWithoutCoverage_DoesNotClaimDeletion()
    {
        var fixture = Fixture.Create();
        var envelope = Receipt(fixture.Peer, keyEpoch: 1, coverage: null);

        MSTestAssert.IsFalse(UserTombstoneGarbageCollector.Covers(envelope, fixture.Reference));
    }

    [TestMethod]
    [TestCategory("Backend")]
    public void ValidateEvidenceStructure_MissingMembershipTransition_FailsClosed()
    {
        var userId = Guid.NewGuid();
        var genesis = StructuralAuthorization(userId, started: 1, ended: null, active: true, genesis: true);
        var addedAtThree = StructuralAuthorization(userId, started: 3, ended: null, active: true, genesis: false);

        MSTestAssert.ThrowsExactly<InvalidDataException>(() =>
            UserTombstoneGarbageCollector.ValidateEvidenceStructure(
                userId,
                currentKeyEpoch: 1,
                currentMembershipEpoch: 3,
                [genesis, addedAtThree],
                [],
                []));
    }

    [TestMethod]
    [TestCategory("Backend")]
    public void ValidateEvidenceStructure_MissingRemovalCutoffEpoch_FailsClosed()
    {
        var userId = Guid.NewGuid();
        var genesis = StructuralAuthorization(userId, started: 1, ended: null, active: true, genesis: true);
        var removed = StructuralAuthorization(userId, started: 2, ended: 3, active: false, genesis: false, maximumKeyEpoch: 2);
        var removalId = removed.RemovalOperationId!.Value;
        var removalHash = removed.RemovalOperationHash!.ToArray();
        var onlyFirstEpoch = new UserOriginRemovalCutoff
        {
            UserId = userId,
            DeviceId = removed.DeviceId,
            OriginInstanceId = removed.OriginInstanceId,
            UserKeyEpoch = 1,
            HighestAcceptedSnapshotRevision = 0,
            HighestAcceptedControlSequence = 0,
            ResultingMembershipEpoch = 3,
            AuthorizationId = removed.AuthorizationId,
            RemovalOperationId = removalId,
            RemovalOperationHash = removalHash
        };

        MSTestAssert.ThrowsExactly<InvalidDataException>(() =>
            UserTombstoneGarbageCollector.ValidateEvidenceStructure(
                userId,
                currentKeyEpoch: 2,
                currentMembershipEpoch: 3,
                [genesis, removed],
                [onlyFirstEpoch],
                []));
    }

    private static UserMembershipAuthorization StructuralAuthorization(
        Guid userId,
        long started,
        long? ended,
        bool active,
        bool genesis,
        long maximumKeyEpoch = 1)
    {
        var signKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var operationHash = Enumerable.Range(33, 32).Select(value => (byte)value).ToArray();
        return new UserMembershipAuthorization
        {
            AuthorizationId = Guid.NewGuid(),
            UserId = userId,
            DeviceId = Guid.NewGuid(),
            OriginInstanceId = Guid.NewGuid(),
            SignPublicKey = signKey,
            SignPublicKeyHash = Hashing.SHA256Hash(signKey),
            AgreementPublicKeyHash = Hashing.SHA256Hash([0x41]),
            TlsCertFingerprint = new string('A', 64),
            DeviceType = DeviceType.WindowsPc,
            StartedMembershipEpoch = started,
            EndedMembershipEpoch = ended,
            MinimumKeyEpoch = 1,
            MaximumKeyEpoch = active ? null : maximumKeyEpoch,
            IsActive = active,
            IsGenesis = genesis,
            AdditionOperationId = genesis ? null : Guid.NewGuid(),
            AdditionOperationHash = genesis ? null : Hashing.SHA256Hash([0x42]),
            RemovalOperationId = active ? null : Guid.NewGuid(),
            RemovalOperationHash = active ? null : operationHash,
            EndedAtUtc = active ? null : DateTimeOffset.UtcNow
        };
    }

    private static UserMembershipAuthorization Authorization(
        Guid deviceId,
        Guid instanceId,
        long started,
        long? ended,
        bool active) => new()
    {
        AuthorizationId = Guid.NewGuid(),
        UserId = Guid.Empty,
        DeviceId = deviceId,
        OriginInstanceId = instanceId,
        StartedMembershipEpoch = started,
        EndedMembershipEpoch = ended,
        MinimumKeyEpoch = 1,
        MaximumKeyEpoch = null,
        IsActive = active
    };

    private static UserRevisionKnowledge Knowledge(TombstoneCausalReference reference, long stored, long merged) => new()
    {
        UserId = Guid.Empty,
        OriginDeviceId = reference.OriginDeviceId,
        OriginInstanceId = reference.OriginInstanceId,
        UserKeyEpoch = reference.UserKeyEpoch,
        HighestStoredRevision = stored,
        HighestMergedRevision = merged
    };

    private static UserSnapshotEnvelope Receipt(
        Member member,
        long keyEpoch,
        TombstoneCausalReference? coverage) => new()
    {
        UserId = Guid.Empty,
        OriginDeviceId = member.DeviceId,
        OriginInstanceId = member.InstanceId,
        OriginRevision = 20,
        UserKeyEpoch = keyEpoch,
        MembershipEpoch = 2,
        Coverage = coverage is null
            ? []
            :
            [
                new UserSnapshotCoverageEntry
                {
                    OriginDeviceId = coverage.OriginDeviceId,
                    OriginInstanceId = coverage.OriginInstanceId,
                    UserKeyEpoch = coverage.UserKeyEpoch,
                    OriginRevision = coverage.OriginRevision
                }
            ]
    };

    private sealed class Fixture
    {
        private Fixture(bool includeOrigin, bool includePeer)
        {
            UserId = Guid.NewGuid();
            Origin = new Member(Guid.NewGuid(), Guid.NewGuid());
            Peer = new Member(Guid.NewGuid(), Guid.NewGuid());
            Version = new SyncVersionStamp
            {
                PhysicalTimeUnixMilliseconds = 1_700_000_000_000,
                LogicalCounter = 3,
                OriginDeviceId = Origin.DeviceId,
                OriginInstanceId = Origin.InstanceId
            };
            Reference = new TombstoneCausalReference
            {
                OriginDeviceId = Origin.DeviceId,
                OriginInstanceId = Origin.InstanceId,
                UserKeyEpoch = 1,
                MembershipEpoch = 2,
                OriginRevision = 7
            };
            Descriptor = new UserTombstoneGarbageCollector.TombstoneDescriptor(
                TombstoneItemType.Password,
                Guid.NewGuid(),
                Version,
                Reference,
                () => UserDataBlobKind.Passwords);

            if (includeOrigin)
                Authorizations.Add(CreateAuthorization(Origin));
            if (includePeer)
                Authorizations.Add(CreateAuthorization(Peer));
            Knowledge[AnchorKey] = CreateKnowledge(Reference, 7, 7);
        }

        public Guid UserId { get; }
        public Member Origin { get; }
        public Member Peer { get; }
        public SyncVersionStamp Version { get; }
        public TombstoneCausalReference Reference { get; }
        public UserTombstoneGarbageCollector.TombstoneDescriptor Descriptor { get; }
        public List<UserMembershipAuthorization> Authorizations { get; } = [];
        public Dictionary<Guid, UserOriginRemovalCutoff[]> Cutoffs { get; } = [];
        public Dictionary<(Guid DeviceId, Guid OriginInstanceId, long KeyEpoch), UserRevisionKnowledge> Knowledge { get; } = [];
        public (Guid DeviceId, Guid OriginInstanceId, long KeyEpoch) AnchorKey =>
            (Reference.OriginDeviceId, Reference.OriginInstanceId, Reference.UserKeyEpoch);

        public static Fixture Create(bool includeOrigin = true, bool includePeer = true) =>
            new(includeOrigin, includePeer);

        public UserTombstoneGarbageCollector.StabilityEvaluation Evaluate(
            IReadOnlyList<UserMembershipAuthorization>? authorizations = null,
            IReadOnlyDictionary<(Guid DeviceId, Guid OriginInstanceId), List<UserSnapshotEnvelope>>? receipts = null) =>
            UserTombstoneGarbageCollector.Evaluate(
                UserId,
                Descriptor,
                authorizations ?? Authorizations,
                Cutoffs,
                Knowledge,
                receipts ?? new Dictionary<(Guid, Guid), List<UserSnapshotEnvelope>>());

        public Dictionary<(Guid DeviceId, Guid OriginInstanceId), List<UserSnapshotEnvelope>> ReceiptsFor(params Member[] members) =>
            ReceiptsForKeyEpoch(1, members);

        public Dictionary<(Guid DeviceId, Guid OriginInstanceId), List<UserSnapshotEnvelope>> ReceiptsForKeyEpoch(
            long keyEpoch,
            params Member[] members) =>
            members.ToDictionary(
                member => (member.DeviceId, member.InstanceId),
                member => new List<UserSnapshotEnvelope> { Receipt(member, keyEpoch, Reference) });

        public void AddRemovalEvidence(UserMembershipAuthorization authorization, long acceptedRevision, long? mergedRevision)
        {
            var operationId = Guid.NewGuid();
            var operationHash = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
            authorization.UserId = UserId;
            authorization.RemovalOperationId = operationId;
            authorization.RemovalOperationHash = operationHash;
            Cutoffs[authorization.AuthorizationId] =
            [
                new UserOriginRemovalCutoff
                {
                    UserId = UserId,
                    DeviceId = authorization.DeviceId,
                    OriginInstanceId = authorization.OriginInstanceId,
                    UserKeyEpoch = 1,
                    HighestAcceptedSnapshotRevision = acceptedRevision,
                    ResultingMembershipEpoch = authorization.EndedMembershipEpoch!.Value,
                    AuthorizationId = authorization.AuthorizationId,
                    RemovalOperationId = operationId,
                    RemovalOperationHash = operationHash.ToArray()
                }
            ];
            if (mergedRevision is long merged)
            {
                Knowledge[(authorization.DeviceId, authorization.OriginInstanceId, 1)] = new UserRevisionKnowledge
                {
                    UserId = UserId,
                    OriginDeviceId = authorization.DeviceId,
                    OriginInstanceId = authorization.OriginInstanceId,
                    UserKeyEpoch = 1,
                    HighestStoredRevision = acceptedRevision,
                    HighestMergedRevision = merged
                };
            }
        }

        private UserMembershipAuthorization CreateAuthorization(Member member)
        {
            var authorization = Authorization(member.DeviceId, member.InstanceId, 1, null, true);
            authorization.UserId = UserId;
            return authorization;
        }

        private UserRevisionKnowledge CreateKnowledge(TombstoneCausalReference reference, long stored, long merged)
        {
            var knowledge = UserTombstoneGarbageCollectorTests.Knowledge(reference, stored, merged);
            knowledge.UserId = UserId;
            return knowledge;
        }
    }

    private sealed record Member(Guid DeviceId, Guid InstanceId);
}
