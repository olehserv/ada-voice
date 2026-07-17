namespace AdaVoice.Core.Storage;

/// <summary>Shared best-effort file helpers used across the storage layer's temp-file write pattern
/// (write to <c>.tmp</c>, then move over the original) — cleanup of a leftover temp file must never
/// throw, since the original failure being handled is what matters.</summary>
internal static class FileOps
{
    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
