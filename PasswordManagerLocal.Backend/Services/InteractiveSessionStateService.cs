using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;

namespace PasswordManagerLocal.Backend.Services;

public sealed class InteractiveSessionStateService : IInteractiveSessionStateService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly object _stateLock = new();
    private TaskCompletionSource? _operationsDrained;
    private int _activeOperations;
    private bool _active;

    public InteractiveSessionStateService(IServiceScopeFactory scopeFactory) =>
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));

    public bool IsActive
    {
        get
        {
            lock (_stateLock)
                return _active;
        }
    }

    public Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_stateLock)
        {
            if (_active)
                throw new InvalidOperationException("Interactive session state is already active.");

            if (_activeOperations != 0)
                throw new InvalidOperationException("Interactive session state is still deactivating.");

            _active = true;
        }

        return Task.CompletedTask;
    }

    public Task DeactivateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Task? operationsDrained = null;
        lock (_stateLock)
        {
            _active = false;
            if (_activeOperations != 0)
            {
                _operationsDrained ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                operationsDrained = _operationsDrained.Task;
            }
        }

        return operationsDrained is null
            ? Task.CompletedTask
            : operationsDrained.WaitAsync(cancellationToken);
    }

    public T ExecuteRequired<T>(Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        BeginRequiredOperation();
        try
        {
            return operation();
        }
        finally
        {
            EndOperation();
        }
    }

    public bool TryGetUserEncryptionKey(Guid userId, out EncryptionKey? key)
    {
        key = null;
        if (!TryBeginOperation())
            return false;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var keys = scope.ServiceProvider.GetRequiredService<IKeyVaultService>();

            foreach (var token in tokens.ListTokensByUid(userId))
            {
                if (keys.TryGetEncryptionKey(token, out key))
                    return true;
            }

            return false;
        }
        finally
        {
            EndOperation();
        }
    }

    public Task LogoutUserAsync(
        Guid userId,
        AuthSessionInvalidationReason reason,
        CancellationToken cancellationToken = default) =>
        ExecuteIfActiveAsync(
            (services, _) =>
            {
                services.GetRequiredService<IAuthSessionService>().LogoutUser(userId, reason);
                return Task.CompletedTask;
            },
            cancellationToken);

    public Task RefreshSyncedUserSessionsAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        return ExecuteIfActiveAsync(
            (services, token) => services
                .GetRequiredService<IAuthSessionService>()
                .RefreshSyncedUserSessionsAsync(user, token),
            cancellationToken);
    }

    public Task RefreshOrInvalidateUserSessionsAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        return ExecuteIfActiveAsync(
            (services, token) => services
                .GetRequiredService<IUserRecoverySessionService>()
                .RefreshOrInvalidateAsync(user, token),
            cancellationToken);
    }

    public Task InvalidateUserCacheAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        ExecuteIfActiveAsync(
            (services, _) =>
            {
                var tokens = services.GetRequiredService<ITokenService>();
                var cache = services.GetRequiredService<IDataCachingService>();
                foreach (var token in tokens.ListTokensByUid(userId))
                    cache.InvalidateToken(token);

                return Task.CompletedTask;
            },
            cancellationToken);

    private async Task ExecuteIfActiveAsync(
        Func<IServiceProvider, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryBeginOperation())
            return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await operation(scope.ServiceProvider, cancellationToken);
        }
        finally
        {
            EndOperation();
        }
    }

    private bool TryBeginOperation()
    {
        lock (_stateLock)
        {
            if (!_active)
                return false;

            _activeOperations++;
            return true;
        }
    }

    private void BeginRequiredOperation()
    {
        lock (_stateLock)
        {
            if (!_active)
            {
                throw new InvalidOperationException(
                    "Interactive user-data state is unavailable without an active interactive session.");
            }

            _activeOperations++;
        }
    }

    private void EndOperation()
    {
        TaskCompletionSource? operationsDrained = null;
        lock (_stateLock)
        {
            _activeOperations--;
            if (!_active && _activeOperations == 0)
            {
                operationsDrained = _operationsDrained;
                _operationsDrained = null;
            }
        }

        operationsDrained?.TrySetResult();
    }
}
