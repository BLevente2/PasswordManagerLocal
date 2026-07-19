using System.Text.Json.Serialization;

using PasswordManagerLocal.Test.TestInfrastructure.Serialization;
namespace PasswordManagerLocal.Test.Backend.Utils;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(CodecPayload), TypeInfoPropertyName = "CodecPayload")]
internal partial class DataCodecTestJsonContext : JsonSerializerContext;
