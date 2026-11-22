using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>
/// Guards the phase-1 boundary: Core owns the semantics and must not depend on
/// any runtime adapter, console or UI project. Dependencies are checked on the
/// declared project graph so compiler-elided assembly references cannot hide a
/// violation.
/// </summary>
public sealed class BoundaryTests
{
    private static readonly string[] ProjectsDependingOnCore =
    [
        "src/Gocordis.Plugins/Gocordis.Plugins.csproj",
        "src/Gocordis.Runtime.Managed/Gocordis.Runtime.Managed.csproj",
        "src/Gocordis.Runtime.Wasm/Gocordis.Runtime.Wasm.csproj",
        "src/Gocordis.Runtime.Process/Gocordis.Runtime.Process.csproj",
        "tests/Gocordis.Conformance.Tests/Gocordis.Conformance.Tests.csproj",
        "tests/Gocordis.Runtime.Tests/Gocordis.Runtime.Tests.csproj",
    ];

    [Fact]
    public void CoreDeclaresNoProjectReferences()
    {
        var project = ReadProject("src/Gocordis.Core/Gocordis.Core.csproj");

        Assert.DoesNotContain("ProjectReference", project, StringComparison.Ordinal);
        // Mature third-party packages (NuGet.Versioning) are allowed in Core;
        // concrete runtimes, console and UI are not.
        Assert.DoesNotContain("Gocordis.Runtime.", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Gocordis.Console", project, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("src/Gocordis.Plugins/Gocordis.Plugins.csproj")]
    [InlineData("src/Gocordis.Runtime.Managed/Gocordis.Runtime.Managed.csproj")]
    [InlineData("src/Gocordis.Runtime.Wasm/Gocordis.Runtime.Wasm.csproj")]
    [InlineData("src/Gocordis.Runtime.Process/Gocordis.Runtime.Process.csproj")]
    [InlineData("tests/Gocordis.Conformance.Tests/Gocordis.Conformance.Tests.csproj")]
    [InlineData("tests/Gocordis.Runtime.Tests/Gocordis.Runtime.Tests.csproj")]
    public void DeclaredGraphPointsToCore(string relativePath)
    {
        Assert.Contains("Gocordis.Core.csproj", ReadProject(relativePath), StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionProjectsDoNotReferenceTests()
    {
        var production = ProjectsDependingOnCore.Take(4);

        foreach (var path in production)
        {
            Assert.DoesNotContain("tests/", ReadProject(path), StringComparison.Ordinal);
        }
    }

    private static string ReadProject(string relativePath)
    {
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Gocordis.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Gocordis.sln not found above the test output directory.");
    }
}
