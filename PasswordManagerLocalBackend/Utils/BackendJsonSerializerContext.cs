using PasswordManagerLocalBackend.Models.Encrypted;
using System.Text.Json.Serialization;

namespace PasswordManagerLocalBackend.Utils;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(UserData))]
internal partial class BackendJsonSerializerContext : JsonSerializerContext;
