using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Sync;
using System.Text.Json.Serialization;

namespace PasswordManagerLocal.Backend.Utils;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(UserData))]
[JsonSerializable(typeof(GeneralUserData))]
[JsonSerializable(typeof(UserPasswordsData))]
[JsonSerializable(typeof(UserDevicesData))]
[JsonSerializable(typeof(DeletedPasswordData))]
[JsonSerializable(typeof(CustomUserColor))]
[JsonSerializable(typeof(DeletedCustomUserColorData))]
[JsonSerializable(typeof(PasswordTag))]
[JsonSerializable(typeof(DeletedPasswordTagData))]
[JsonSerializable(typeof(DeletedUserDeviceData))]
[JsonSerializable(typeof(DeviceEnrollmentSnapshot), TypeInfoPropertyName = "DeviceEnrollmentSnapshot")]
[JsonSerializable(typeof(SyncDeltaPayload), TypeInfoPropertyName = "SyncDeltaPayload")]
[JsonSerializable(typeof(UserSnapshotEnvelope), TypeInfoPropertyName = "UserSnapshotEnvelope")]
[JsonSerializable(typeof(UserControlOperationEnvelope), TypeInfoPropertyName = "UserControlOperationEnvelope")]
[JsonSerializable(typeof(KeyEpochReplacementPayload), TypeInfoPropertyName = "KeyEpochReplacementPayload")]
[JsonSerializable(typeof(DeviceAdditionPayload), TypeInfoPropertyName = "DeviceAdditionPayload")]
[JsonSerializable(typeof(DeviceRemovalPayload), TypeInfoPropertyName = "DeviceRemovalPayload")]
internal partial class BackendJsonSerializerContext : JsonSerializerContext;
