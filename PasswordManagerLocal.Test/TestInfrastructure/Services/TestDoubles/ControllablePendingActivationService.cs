using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSec.Cryptography;
using NSecKey = NSec.Cryptography.Key;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using PasswordManagerLocal.Test.Fakes;
using PasswordManagerLocal.Test.TestInfrastructure;
using System.Text.Json;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

using PasswordManagerLocal.Test.TestInfrastructure.Services.Fixtures;
using PasswordManagerLocal.Test.TestInfrastructure.Services.TestDoubles;

namespace PasswordManagerLocal.Test.TestInfrastructure.Services.TestDoubles;

internal sealed class ControllablePendingActivationService : IPendingSyncActivationService
{
    private readonly IPendingSyncActivationService _inner;

    public ControllablePendingActivationService(IPendingSyncActivationService inner) => _inner = inner;

    public bool FailNextActivation { get; set; }

    public void ActivateDevices(IReadOnlyList<Device> devices) => _inner.ActivateDevices(devices);

    public Task ActivatePendingAsync(CancellationToken ct = default)
    {
        if (FailNextActivation)
        {
            FailNextActivation = false;
            throw new InvalidOperationException("Injected post-commit activation failure.");
        }

        return _inner.ActivatePendingAsync(ct);
    }
}
