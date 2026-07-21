using PasswordManagerLocal.Windows.Ipc.Client;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Serialization;
using PasswordManagerLocal.Windows.Ipc.Transport;
using PasswordManagerLocal.Windows.Ipc.Validation;

namespace PasswordManagerLocal.Windows.AgentConnection;

public sealed class WindowsAgentControlConnector : IWindowsAgentControlConnector
{
    public async Task<IWindowsAgentRegisteredConnection?> TryConnectAndRegisterAsync(
        string pipeName,
        TimeSpan connectTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        if (connectTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(connectTimeout));

        var serializer = new WindowsIpcSerializer();
        var validator = new WindowsIpcContractValidator();
        using var timeoutSource = new CancellationTokenSource(connectTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        IWindowsIpcConnection? connection = null;
        WindowsIpcClient? client = null;
        try
        {
            connection = await new WindowsNamedPipeClient(
                pipeName,
                new IpcFrameCodec()).ConnectAsync(linkedSource.Token);
            client = new WindowsIpcClient(
                connection,
                serializer,
                new WindowsIpcClientOptions(
                    IpcPeerRole.Ui,
                    IpcPeerRole.Agent,
                    IpcCapabilities.Control | IpcCapabilities.Status | IpcCapabilities.UiActivation,
                    Environment.ProcessId,
                    Guid.NewGuid(),
                    maximumPendingRequests: 16),
                validator);
            connection = null;
            await client.HandshakeAsync(linkedSource.Token);
            var controlClient = new WindowsIpcControlClient(client, serializer, validator);
            var registration = await controlClient.RegisterUiConnectionAsync(linkedSource.Token);
            if (!registration.IsRegistered)
                throw new InvalidOperationException("The agent did not register the UI connection.");

            var registered = new WindowsAgentRegisteredConnection(client, controlClient);
            client = null;
            return registered;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (client is not null)
            {
                try { await client.DisposeAsync(); } catch { }
            }
            else if (connection is not null)
            {
                try { await connection.DisposeAsync(); } catch { }
            }
        }
    }
}
