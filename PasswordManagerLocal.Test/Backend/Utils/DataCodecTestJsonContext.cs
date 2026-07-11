using System.Text.Json.Serialization;

namespace PasswordManagerLocal.Test.Backend.Utils;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(DataCodecTests.CodecPayload), TypeInfoPropertyName = "CodecPayload")]
internal partial class DataCodecTestJsonContext : JsonSerializerContext;
