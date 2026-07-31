using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PasswordManagerLocal.Windows.Ipc.Test.Dependency;

[TestClass]
public sealed class Phase9ArchitectureGuardTests
{
    [TestMethod]
    public void ContractsRemainsPlatformIndependentAndDependencyFree()
    {
        var projectPath = Path.Combine(
            GetRepositoryRoot(),
            "PasswordManagerLocal.Contracts",
            "PasswordManagerLocal.Contracts.csproj");
        var project = XDocument.Load(projectPath);

        Assert.AreEqual("net10.0", Property(project, "TargetFramework"));
        Assert.IsEmpty(project.Descendants("ProjectReference").ToArray());
        Assert.IsEmpty(project.Descendants("PackageReference").ToArray());
        Assert.IsFalse(File.ReadAllText(projectPath).Contains("-windows", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ProductionProjectReferenceGraphHasNoCycle()
    {
        var root = GetRepositoryRoot();
        var projects = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !ContainsGeneratedDirectory(path))
            .ToArray();
        var graph = projects.ToDictionary(
            project => Path.GetFullPath(project),
            project => ProjectReferences(project)
                .Select(reference => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, reference)))
                .Where(File.Exists)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trail = new Stack<string>();

        foreach (var project in graph.Keys)
            Visit(project, graph, visiting, visited, trail);
    }

    [TestMethod]
    public void WindowsUiAndAgentProjectReferencesPreserveOwnershipBoundaries()
    {
        var root = GetRepositoryRoot();
        var ui = ProjectReferenceNames(Path.Combine(
            root,
            "PasswordManagerLocal",
            "PasswordManagerLocal.Windows",
            "PasswordManagerLocal.Windows.csproj"));
        var agent = ProjectReferenceNames(Path.Combine(
            root,
            "PasswordManagerLocal.Windows.Agent",
            "PasswordManagerLocal.Windows.Agent.csproj"));

        CollectionAssert.DoesNotContain(ui, "PasswordManagerLocal.Backend.Hosting.csproj");
        CollectionAssert.DoesNotContain(ui, "PasswordManagerLocal.Backend.Windows.csproj");
        CollectionAssert.Contains(agent, "PasswordManagerLocal.Backend.Hosting.csproj");
        CollectionAssert.Contains(agent, "PasswordManagerLocal.Backend.Windows.csproj");
        CollectionAssert.DoesNotContain(agent, "PasswordManagerLocal.Frontend.csproj");
    }


    [TestMethod]
    public void AgentRemainsTheOnlyWindowsStartupRegistryWriter()
    {
        var root = GetRepositoryRoot();
        var callers = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !ContainsGeneratedDirectory(path))
            .Where(path => !path.Contains(".Test", StringComparison.Ordinal))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}PasswordManagerLocal.Android{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains(
                "new WindowsRunStartupRegistration(",
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { Path.Combine("PasswordManagerLocal.Windows.Agent", "Program.cs") },
            callers);
    }

    [TestMethod]
    public void ProcessTestHostIsReferencedOnlyByTheWindowsIpcTestProject()
    {
        var root = GetRepositoryRoot();
        var referencingProjects = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !ContainsGeneratedDirectory(path))
            .Where(path => ProjectReferences(path).Any(reference =>
                reference.EndsWith(
                    "PasswordManagerLocal.Windows.TestHost.csproj",
                    StringComparison.OrdinalIgnoreCase)))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { Path.Combine("PasswordManagerLocal.Windows.Ipc.Test", "PasswordManagerLocal.Windows.Ipc.Test.csproj") },
            referencingProjects);
    }


    [TestMethod]
    public void SourceFilesUseOneMatchingPrimaryType()
    {
        var root = GetRepositoryRoot();
        var declarationPattern = new Regex(
            @"^(?:public|internal)\s+(?:sealed\s+|abstract\s+|partial\s+|static\s+|readonly\s+)*(?:class|record(?:\s+class|\s+struct)?|struct|interface|enum)\s+(?<name>\w+)",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !ContainsGeneratedDirectory(path)))
        {
            var names = declarationPattern.Matches(File.ReadAllText(path))
                .Select(match => match.Groups["name"].Value)
                .ToArray();
            Assert.IsTrue(
                names.Length <= 1,
                $"Multiple top-level types in {Path.GetRelativePath(root, path)}: {string.Join(", ", names)}");
            if (names.Length == 0)
                continue;

            var expectedName = Path.GetFileNameWithoutExtension(path);
            if (expectedName.EndsWith(".axaml", StringComparison.Ordinal))
                expectedName = Path.GetFileNameWithoutExtension(expectedName);
            Assert.AreEqual(
                expectedName,
                names[0],
                Path.GetRelativePath(root, path));
        }
    }

    [TestMethod]
    public void DocumentationMarkdownRemainsUnderDocsExceptForRootReadme()
    {
        var root = GetRepositoryRoot();
        var outsideDocs = Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Where(path => !ContainsGeneratedDirectory(path))
            .Select(path => Path.GetRelativePath(root, path))
            .Where(path => !path.Equals("README.md", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.StartsWith(
                $"Docs{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .ToArray();

        Assert.IsEmpty(outsideDocs, string.Join(Environment.NewLine, outsideDocs));
    }


    [TestMethod]
    public void WindowsPublishEntryPointUsesTheActualUiProjectAndLayoutVerifier()
    {
        var root = GetRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "Tools", "PublishWindowsX64.ps1"));

        StringAssert.Contains(
            source,
            @"PasswordManagerLocal\PasswordManagerLocal.Windows\PasswordManagerLocal.Windows.csproj");
        StringAssert.Contains(source, @"Tools\Windows\VerifyWindowsPublishedLayout.ps1");
        Assert.IsFalse(source.Contains("$publishScript", StringComparison.Ordinal));
    }

    [TestMethod]
    public void WindowsUiProjectDefinesRepositoryRootForThePublishOptimizer()
    {
        var projectPath = Path.Combine(
            GetRepositoryRoot(),
            "PasswordManagerLocal",
            "PasswordManagerLocal.Windows",
            "PasswordManagerLocal.Windows.csproj");
        var project = XDocument.Load(projectPath);
        var repositoryRoot = project.Descendants("RepositoryRoot").Single();
        var optimizer = project.Descendants("Exec").Single(element =>
            (element.Attribute("Command")?.Value ?? string.Empty).Contains(
                "OptimizeWindowsPublish.ps1",
                StringComparison.Ordinal));

        Assert.IsFalse(string.IsNullOrWhiteSpace(repositoryRoot.Value));
        StringAssert.Contains(optimizer.Attribute("Command")!.Value, "$(RepositoryRoot)");
    }

    [TestMethod]
    public void WindowsProductionSourcesContainNoTestHostOrProductionTestModeReference()
    {
        var root = GetRepositoryRoot();
        var productionSources = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !ContainsGeneratedDirectory(path))
            .Where(path => !path.Contains(".Test", StringComparison.Ordinal))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}PasswordManagerLocal.Windows.TestHost{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();

        Assert.IsFalse(productionSources.Any(source =>
            source.Contains("PasswordManagerLocal.Windows.TestHost", StringComparison.Ordinal)));
        Assert.IsFalse(productionSources.Any(source =>
            source.Contains("PHASE9_TEST_MODE", StringComparison.Ordinal)));
    }

    private static void Visit(
        string project,
        IReadOnlyDictionary<string, string[]> graph,
        ISet<string> visiting,
        ISet<string> visited,
        Stack<string> trail)
    {
        if (visited.Contains(project))
            return;
        if (!visiting.Add(project))
        {
            var cycle = trail.Reverse().Append(project)
                .Select(Path.GetFileName)
                .ToArray();
            Assert.Fail($"Project-reference cycle: {string.Join(" -> ", cycle)}");
        }

        trail.Push(project);
        if (graph.TryGetValue(project, out var references))
        {
            foreach (var reference in references)
                Visit(reference, graph, visiting, visited, trail);
        }
        trail.Pop();
        visiting.Remove(project);
        visited.Add(project);
    }

    private static string[] ProjectReferences(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();

    private static string[] ProjectReferenceNames(string projectPath) =>
        ProjectReferences(projectPath).Select(Path.GetFileName).ToArray();

    private static string? Property(XDocument project, string name) =>
        project.Descendants(name).Select(element => element.Value).FirstOrDefault();

    private static bool ContainsGeneratedDirectory(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFilePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));
}
