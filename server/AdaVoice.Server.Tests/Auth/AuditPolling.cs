namespace AdaVoice.Server.Tests.Auth;

/// <summary>Audit rows are persisted by a background flush (<c>AuditFlushService</c>), not
/// synchronously with the request that triggered them — so a test asserting on audit state
/// must poll instead of querying once right after the HTTP call returns. Shared by every
/// integration test that checks audit rows, so each does not hand-roll its own poll loop.</summary>
internal static class AuditPolling
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(200);

    /// <summary>Re-runs <paramref name="query"/> (expected to open its own fresh DbContext read)
    /// until <paramref name="isReady"/> accepts the result or <paramref name="timeout"/> elapses,
    /// then returns whatever the last attempt produced (so a timed-out caller still gets a
    /// useful assertion failure instead of an unrelated exception).</summary>
    public static async Task<T> UntilAsync<T>(Func<Task<T>> query, Func<T, bool> isReady, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow.Add(timeout ?? DefaultTimeout);
        T result;
        do
        {
            result = await query();
            if (isReady(result))
            {
                return result;
            }

            await Task.Delay(Interval);
        }
        while (DateTime.UtcNow < deadline);

        return result;
    }
}
