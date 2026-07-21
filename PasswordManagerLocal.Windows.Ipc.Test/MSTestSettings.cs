using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace PasswordManagerLocal.Windows.Ipc.Test;

internal static class MSTestSettings
{
}
