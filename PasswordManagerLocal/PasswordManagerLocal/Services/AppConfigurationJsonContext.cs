using System.Text.Json.Serialization;

namespace PasswordManagerLocal.Services;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(AppConfiguration))]
internal partial class AppConfigurationJsonContext : JsonSerializerContext;
