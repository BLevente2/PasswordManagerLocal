using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.Agent.Hosting;
using PasswordManagerLocal.Windows.Activation;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace PasswordManagerLocal.Windows.Ipc.Test.Dependency;

[TestClass]
public sealed class Phase4DependencyBoundaryTests
{
    [TestMethod]
    public void AgentAssemblyHasNoFrontendAvaloniaOrBackendReferences()
    {
        var references = typeof(WindowsAgentHost).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.IsFalse(references.Any(name => name.StartsWith("Avalonia", StringComparison.Ordinal)));
        Assert.IsFalse(references.Any(name => name.StartsWith("PasswordManagerLocal.Frontend", StringComparison.Ordinal)));
        Assert.IsFalse(references.Any(name => name.StartsWith("PasswordManagerLocal.Backend", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void AgentSourceContainsNoBackendRuntimeOrEndpointRpcConstruction()
    {
        var root = GetRepositoryRoot();
        var agentDirectory = Path.Combine(root, "PasswordManagerLocal.Windows.Agent");
        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(agentDirectory, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
        var project = File.ReadAllText(Path.Combine(
            agentDirectory,
            "PasswordManagerLocal.Windows.Agent.csproj"));

        Assert.IsFalse(Regex.IsMatch(
            source,
            @"\bnew\s+(?:(?:global::)?(?:[A-Za-z_][A-Za-z0-9_]*\.)*)BackendRuntime\s*\(",
            RegexOptions.CultureInvariant));
        Assert.IsFalse(source.Contains("BackendRuntimeFactory", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("BackendRuntimeLifetimeCoordinator", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("InProcessFrontendBackendClient", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IBackendRuntime", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IEndpoints", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("Avalonia", StringComparison.Ordinal));
        Assert.IsFalse(project.Contains("PasswordManagerLocal.Frontend", StringComparison.Ordinal));
        Assert.IsFalse(project.Contains("PasswordManagerLocal.Backend", StringComparison.Ordinal));
        Assert.IsFalse(project.Contains("PasswordManagerLocal.Windows.csproj", StringComparison.Ordinal));
    }

    [TestMethod]
    public void WindowsUiAcquiresOwnershipBeforeItsSingleBackendRuntimeConstruction()
    {
        var root = GetRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(
            root,
            "PasswordManagerLocal",
            "PasswordManagerLocal.Windows",
            "Program.cs"));
        var lockIndex = program.IndexOf("names.UiLockFilePath", StringComparison.Ordinal);
        var roleCheckIndex = program.IndexOf(
            "instanceRole != WindowsUiInstanceRole.Primary",
            StringComparison.Ordinal);
        var runtimeIndex = program.IndexOf(
            "WindowsBackendRuntimeFactory.Create()",
            StringComparison.Ordinal);

        Assert.IsTrue(lockIndex >= 0);
        Assert.IsTrue(roleCheckIndex > lockIndex);
        Assert.IsTrue(runtimeIndex > roleCheckIndex);
        Assert.AreEqual(
            1,
            CountOccurrences(program, "WindowsBackendRuntimeFactory.Create()"));
    }

    [TestMethod]
    public void WindowsUiAssemblyRetainsBackendOwnershipAndActivationBridge()
    {
        var references = typeof(AvaloniaWindowActivationBridge).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.IsTrue(references.Contains("PasswordManagerLocal.Backend.Hosting"));
        Assert.IsTrue(references.Contains("PasswordManagerLocal.Backend.Windows"));
        Assert.IsTrue(references.Contains("PasswordManagerLocal.Windows.Ipc"));
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static string GetRepositoryRoot([CallerFilePath] string sourceFilePath = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFilePath)!,
            "..",
            ".."));
}
