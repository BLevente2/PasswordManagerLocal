namespace PasswordManagerLocal.Windows.Ipc.Protocol;

public enum IpcOperationId
{
    Ping = 1,
    GetAgentStatus = 10,
    GetBackendRuntimeStatus = 11,
    GetInteractiveSessionStatus = 12,
    GetSynchronizationStatus = 13,
    RegisterUiConnection = 20,
    UnregisterUiConnection = 21,
    RequestUiOpen = 22,
    RequestUiActivation = 23,
    RequestAgentExit = 24,
    GetBackgroundSyncSettings = 30,
    SetBackgroundSyncSettings = 31,
    EndpointRpcRequest = 40
}
