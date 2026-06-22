namespace AdaVoice.Core.Storage;

/// <summary>The on-disk layout under the AdaVoice data root (design 04 §2).</summary>
public static class AdaVoicePaths
{
    /// <summary><c>%LOCALAPPDATA%\AdaVoice</c> — the default data root (configurable later).</summary>
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AdaVoice");

    public static string LibraryFile(string root) => Path.Combine(root, "library.json");

    public static string AudioDir(string root) => Path.Combine(root, "audio");

    public static string AudioPath(string root, string fileName) => Path.Combine(AudioDir(root), fileName);
}
