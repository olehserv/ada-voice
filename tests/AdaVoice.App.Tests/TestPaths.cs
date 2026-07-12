using System.IO;

namespace AdaVoice.App.Tests;

public static class TestPaths
{
    public static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (dir.GetFiles("*.slnx").Length != 0 
                || dir.GetDirectories(".git").Length != 0)
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find solution root.");
    }

    public static string ScreenshotDirectory(string group = "after")
    {
        var root = FindSolutionRoot();
        var path = Path.Combine(root, "docs", "ui", "screenshots", group);
        Directory.CreateDirectory(path);
        return path;
    }
}
