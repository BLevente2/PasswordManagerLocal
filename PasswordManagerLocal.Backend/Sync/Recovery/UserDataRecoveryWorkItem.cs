using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace PasswordManagerLocal.Backend.Sync.Recovery;

internal sealed record UserDataRecoveryWorkItem(
    Guid UserId,
    UserDataRecoveryTrigger Trigger);
