using PasswordManagerLocal.Contracts.Runtime;
using PasswordManagerLocal.Contracts.BackgroundSync;
using System.Text.Json.Serialization;

namespace PasswordManagerLocal.Backend.Hosting;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(BackgroundSyncSettings))]
internal partial class BackgroundSyncSettingsJsonContext : JsonSerializerContext;
