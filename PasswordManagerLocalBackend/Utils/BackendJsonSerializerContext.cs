using PasswordManagerLocalBackend.Models.Encrypted;
using System.Text.Json.Serialization;

namespace PasswordManagerLocalBackend.Utils;

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
internal partial class BackendJsonSerializerContext : JsonSerializerContext;
