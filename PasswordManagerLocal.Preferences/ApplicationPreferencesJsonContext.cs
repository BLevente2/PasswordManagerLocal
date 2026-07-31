using PasswordManagerLocal.Contracts.Preferences;
using System.Text.Json.Serialization;

namespace PasswordManagerLocal.Preferences;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ApplicationPreferences))]
internal partial class ApplicationPreferencesJsonContext : JsonSerializerContext;
