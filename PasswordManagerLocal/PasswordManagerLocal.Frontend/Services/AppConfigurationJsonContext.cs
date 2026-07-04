using System.Text.Json.Serialization;

namespace PasswordManagerLocal.Frontend.Services;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(AppConfiguration))]
internal partial class AppConfigurationJsonContext : JsonSerializerContext;
