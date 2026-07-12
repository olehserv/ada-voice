using System.Xml.Linq;

namespace AdaVoice.Server.Tests.Architecture;

// The server dependency direction is a locked constraint of the monetization design
// (Api -> Infrastructure -> Domain; Workers -> Infrastructure). This guard parses the
// project files so a later phase cannot quietly add a forbidden reference (for example
// Domain depending on Infrastructure, or a lower layer depending on Api/Workers).
public class DependencyDirectionTests
{
    private static readonly string ServerDir = LocateServerDir();

    [Fact]
    public void Domain_has_no_project_references()
    {
        Assert.Empty(ProjectReferencesOf("AdaVoice.Server.Domain"));
    }

    [Fact]
    public void Infrastructure_references_only_Domain()
    {
        Assert.Equal(new[] { "AdaVoice.Server.Domain" }, ProjectReferencesOf("AdaVoice.Server.Infrastructure"));
    }

    [Fact]
    public void Api_references_Infrastructure()
    {
        Assert.Contains("AdaVoice.Server.Infrastructure", ProjectReferencesOf("AdaVoice.Server.Api"));
    }

    [Fact]
    public void Workers_references_Infrastructure()
    {
        Assert.Contains("AdaVoice.Server.Infrastructure", ProjectReferencesOf("AdaVoice.Server.Workers"));
    }

    [Fact]
    public void Workers_does_not_reference_Api()
    {
        // Api hosts the workers as hosted services (Api -> Workers in Phase 8), so Workers
        // must never reference Api back — that would be a dependency cycle.
        Assert.DoesNotContain("AdaVoice.Server.Api", ProjectReferencesOf("AdaVoice.Server.Workers"));
    }

    [Theory]
    [InlineData("AdaVoice.Server.Domain")]
    [InlineData("AdaVoice.Server.Infrastructure")]
    public void Lower_layers_do_not_reference_Api_or_Workers(string project)
    {
        var refs = ProjectReferencesOf(project);
        Assert.DoesNotContain("AdaVoice.Server.Api", refs);
        Assert.DoesNotContain("AdaVoice.Server.Workers", refs);
    }

    private static IReadOnlyList<string> ProjectReferencesOf(string project)
    {
        var path = Path.Combine(ServerDir, project, project + ".csproj");
        var doc = XDocument.Load(path);
        return doc.Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar))
            .Select(name => Path.GetFileNameWithoutExtension(name)!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string LocateServerDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AdaVoice.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"Could not locate repo root (AdaVoice.slnx) from {AppContext.BaseDirectory}.");
        }

        return Path.Combine(dir.FullName, "server");
    }
}
