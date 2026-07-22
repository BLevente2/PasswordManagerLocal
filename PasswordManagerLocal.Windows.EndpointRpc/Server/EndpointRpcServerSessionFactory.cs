using PasswordManagerLocal.Windows.EndpointRpc.Authorization;
using PasswordManagerLocal.Windows.EndpointRpc.Serialization;
using PasswordManagerLocal.Windows.EndpointRpc.Validation;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Serialization;
using PasswordManagerLocal.Windows.Ipc.Server;
using PasswordManagerLocal.Windows.Ipc.Transport;
using PasswordManagerLocal.Windows.Ipc.Validation;

namespace PasswordManagerLocal.Windows.EndpointRpc.Server;

public sealed class EndpointRpcServerSessionFactory : IEndpointRpcServerSessionFactory
{
    public const int MaximumActiveEndpointRequests = 32;

    private readonly WindowsIpcServerConnectionSessionFactory _innerFactory;

    public EndpointRpcServerSessionFactory(
        IEndpointRpcEndpointAdapter endpointAdapter,
        EndpointRpcConnectionAuthorizer connectionAuthorizer)
    {
        ArgumentNullException.ThrowIfNull(endpointAdapter);
        ArgumentNullException.ThrowIfNull(connectionAuthorizer);

        var serializer = new EndpointRpcSerializer();
        var validator = new EndpointRpcContractValidator();
        var codec = new EndpointRpcMessageCodec(serializer);
        var dispatcher = new EndpointRpcDispatcher(
            endpointAdapter,
            serializer,
            validator,
            new EndpointRpcBackendErrorMapper());
        var requestHandler = new EndpointRpcWindowsIpcRequestHandler(
            dispatcher,
            codec,
            validator);
        var ipcDispatcher = new WindowsIpcRequestDispatcher(
            [requestHandler],
            new WindowsIpcContractValidator(),
            new EndpointRpcOperationAuthorizer(connectionAuthorizer));
        var options = new WindowsIpcServerOptions(
            IpcPeerRole.Agent,
            [IpcPeerRole.Ui],
            IpcCapabilities.EndpointRpc,
            MaximumActiveEndpointRequests,
            managesUiRegistration: false,
            requiredClientCapabilities: IpcCapabilities.EndpointRpc);

        _innerFactory = new WindowsIpcServerConnectionSessionFactory(
            new WindowsIpcSerializer(),
            ipcDispatcher,
            options,
            observers: [connectionAuthorizer],
            uiCoordinator: null,
            contractValidator: new WindowsIpcContractValidator(),
            handshakeAuthorizer: connectionAuthorizer);
    }

    public IEndpointRpcServerSession Create(IWindowsIpcConnection connection) =>
        new EndpointRpcServerSession(_innerFactory.Create(connection));
}
