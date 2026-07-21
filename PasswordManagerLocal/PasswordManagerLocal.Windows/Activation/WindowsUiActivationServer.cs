using PasswordManagerLocal.Windows.Ipc.Authorization;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Serialization;
using PasswordManagerLocal.Windows.Ipc.Server;
using PasswordManagerLocal.Windows.Ipc.Transport;
using PasswordManagerLocal.Windows.Ipc.Validation;

namespace PasswordManagerLocal.Windows.Activation;

public sealed class WindowsUiActivationServer : IAsyncDisposable
{
    private readonly IWindowsIpcServerHost _serverHost;

    public WindowsUiActivationServer(IWindowsIpcServerHost serverHost)
    {
        _serverHost = serverHost ?? throw new ArgumentNullException(nameof(serverHost));
    }

    public WindowsUiActivationServer(string pipeName, IWindowsWindowActivationBridge activationBridge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(activationBridge);
        var serializer = new WindowsIpcSerializer();
        var validator = new WindowsIpcContractValidator();
        var dispatcher = new WindowsIpcRequestDispatcher(
            new IWindowsIpcRequestHandler[]
            {
                new RequestUiActivationWindowsIpcRequestHandler(
                    new WindowsUiActivationRequestSink(activationBridge))
            },
            validator,
            new WindowsUiActivationOperationAuthorizer());
        var options = new WindowsIpcServerOptions(
            IpcPeerRole.Ui,
            new[] { IpcPeerRole.Agent, IpcPeerRole.Ui, IpcPeerRole.TestClient },
            IpcCapabilities.UiActivation,
            maximumActiveRequestsPerConnection: 4,
            managesUiRegistration: false);
        var factory = new WindowsIpcServerConnectionSessionFactory(
            serializer,
            dispatcher,
            options,
            contractValidator: validator);
        _serverHost = new WindowsIpcServerHost(
            new WindowsNamedPipeServer(pipeName, new IpcFrameCodec()),
            factory,
            new WindowsIpcServerHostOptions(maximumActiveConnections: 4));
    }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _serverHost.StartAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _serverHost.StopAsync();
        await _serverHost.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
