using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Security;
using System.Security.Cryptography;
using System.Text;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocalTest.Backend.Security;

[TestClass]
public sealed class PassphraseKeyProtectorTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public void ProtectThenUnprotect_RoundTripsBinaryPlaintext()
    {
        using var protector = CreateProtector("primary passphrase");
        var plaintext = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();

        var protectedBlob = protector.Protect(plaintext);
        var restored = protector.Unprotect(protectedBlob);

        CollectionAssert.AreEqual(plaintext, restored);
        MSTestAssert.IsFalse(plaintext.SequenceEqual(protectedBlob));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public void Protect_EmptyPlaintext_RoundTrips()
    {
        using var protector = CreateProtector("empty plaintext passphrase");

        var protectedBlob = protector.Protect(Array.Empty<byte>());
        var restored = protector.Unprotect(protectedBlob);

        MSTestAssert.IsEmpty(restored);
        MSTestAssert.IsTrue(protectedBlob.Length > 0);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public void Protect_SamePlaintextTwice_ProducesDifferentBlobs()
    {
        using var protector = CreateProtector("randomized output passphrase");
        var plaintext = Encoding.UTF8.GetBytes("database key material");

        var first = protector.Protect(plaintext);
        var second = protector.Protect(plaintext);

        MSTestAssert.IsFalse(first.SequenceEqual(second));
        CollectionAssert.AreEqual(plaintext, protector.Unprotect(first));
        CollectionAssert.AreEqual(plaintext, protector.Unprotect(second));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public void Unprotect_WithWrongPassphrase_RejectsBlob()
    {
        using var writer = CreateProtector("correct passphrase");
        using var reader = CreateProtector("incorrect passphrase");
        var protectedBlob = writer.Protect(Encoding.UTF8.GetBytes("secret"));

        ExpectThrows<CryptographicException>(() => reader.Unprotect(protectedBlob));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public void Unprotect_WhenAuthenticationTagIsTampered_RejectsBlob()
    {
        using var protector = CreateProtector("tamper test passphrase");
        var protectedBlob = protector.Protect(Encoding.UTF8.GetBytes("secret"));
        protectedBlob[^1] ^= 0x80;

        ExpectThrows<CryptographicException>(() => protector.Unprotect(protectedBlob));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public void Unprotect_RejectsUnsupportedVersionAndNegativeCiphertextLength()
    {
        using var protector = CreateProtector("format validation passphrase");
        var protectedBlob = protector.Protect(Encoding.UTF8.GetBytes("secret"));

        var unsupportedVersion = protectedBlob.ToArray();
        unsupportedVersion[0] = 2;
        ExpectThrows<CryptographicException>(() => protector.Unprotect(unsupportedVersion));

        var negativeLength = protectedBlob.ToArray();
        var saltLength = negativeLength[1];
        var ciphertextLengthOffset = 2 + saltLength + sizeof(int) + 12;
        negativeLength[ciphertextLengthOffset] = 0xFF;
        negativeLength[ciphertextLengthOffset + 1] = 0xFF;
        negativeLength[ciphertextLengthOffset + 2] = 0xFF;
        negativeLength[ciphertextLengthOffset + 3] = 0xFF;
        ExpectThrows<CryptographicException>(() => protector.Unprotect(negativeLength));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public void Constructor_RejectsUnsafeParameters()
    {
        ExpectThrows<ArgumentException>(() => new PassphraseKeyProtector(Array.Empty<byte>(), 1_000, 16));
        ExpectThrows<ArgumentOutOfRangeException>(() => new PassphraseKeyProtector(new byte[] { 1 }, 0, 16));
        ExpectThrows<ArgumentOutOfRangeException>(() => new PassphraseKeyProtector(new byte[] { 1 }, 1_000, 0));
    }

    private static PassphraseKeyProtector CreateProtector(string passphrase) =>
        new(Encoding.UTF8.GetBytes(passphrase), 1_000, 16);

    private static void ExpectThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
            MSTestAssert.Fail($"Expected exception: {typeof(TException).Name}");
        }
        catch (TException)
        {
        }
    }
}
