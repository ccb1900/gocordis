using System.Xml.Linq;
using Xunit;
using Xunit.Sdk;

namespace Gocordis.Product.Tests;

public sealed class SampleParityTests
{
    // One sample per backend/composition: managed component, process plugin,
    // UI contribution, dedicated HTTP host, business journal, plus the two
    // composition roots that wire the others together.
    private static readonly string[] ExpectedSamples =
    {
        "SamplePlugin", "EchoProcPlugin", "UiPlugin", "HttpdHost", "BackfillApp",
        "PlatformHost", "KoishiCase",
    };

    [Fact]
    public void SampleParityListsExpectedBackendsAndComposition()
    {
        var root = FindRepoRoot();

        foreach (var sample in ExpectedSamples)
        {
            var directory = Path.Combine(root, "samples", sample);
            Assert.True(Directory.Exists(directory), $"missing sample directory: samples/{sample}");

            var projects = Directory.GetFiles(directory, "*.csproj");
            var project = Assert.Single(projects);
            Assert.Equal($"{sample}.csproj", Path.GetFileName(project));
            Assert.Equal("Project", XDocument.Load(project).Root?.Name.LocalName);
            Assert.Contains("<Project", File.ReadAllText(project));
        }

        // The new samples must expose the public entry points the other tests use.
        Assert.True(File.Exists(Path.Combine(root, "samples", "HttpdHost", "HttpdProbeHost.cs")));
        Assert.True(File.Exists(Path.Combine(root, "samples", "BackfillApp", "BackfillJournal.cs")));
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "samples", "SamplePlugin", "SamplePlugin.csproj")))
            {
                return directory.FullName;
            }
        }

        throw new XunitException($"cannot locate samples/ from {AppContext.BaseDirectory}");
    }
}
