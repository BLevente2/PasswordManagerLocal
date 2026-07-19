using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using System.Security.Cryptography;
using System.Text.Json;
using static PasswordManagerLocal.Backend.Utils.DataCodec;

namespace PasswordManagerLocal.Backend.Internal.Recovery;

internal sealed record BlobVerificationResult(bool Success, IDisposable? Value, UserDataBundleVerificationResult? Failure)
{
    public static BlobVerificationResult Verified(IDisposable value) => new(true, value, null);
    public static BlobVerificationResult Failed(UserDataBundleVerificationResult failure) => new(false, null, failure);
}
