using Microsoft.EntityFrameworkCore;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using System.Text.Json;

namespace PasswordManagerLocal.Backend.Internal.Recovery;

internal sealed record EpochResolution(
    bool Success,
    long KeyEpoch,
    long MembershipEpoch,
    UserDataRecoveryState State,
    string? DiagnosticCode)
{
    public static EpochResolution Failed(UserDataRecoveryState state, string diagnosticCode) =>
        new(false, 0, 0, state, diagnosticCode);
}
