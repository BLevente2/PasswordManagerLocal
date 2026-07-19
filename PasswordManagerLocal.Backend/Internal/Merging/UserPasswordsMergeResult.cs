using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Utils;

namespace PasswordManagerLocal.Backend.Internal.Merging;

internal sealed record UserPasswordsMergeResult<TLive, TDeleted>(List<TLive> Live, List<TDeleted> Deleted);
