using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Utils;
using System.Security.Cryptography;
using System.Text;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.TestInfrastructure.Serialization;

internal sealed class CodecPayload
{
    public string Name { get; set; } = string.Empty;
    public byte[] Data { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}
