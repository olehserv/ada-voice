using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AdaVoice.Server.Tests.Auth;

/// <summary>Captures every formatted log message into a shared queue so a test can assert that
/// no secret (password, refresh/access token) is ever written to the logs (§14 #6).</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages;

    public CapturingLoggerProvider(ConcurrentQueue<string> messages) => _messages = messages;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly ConcurrentQueue<string> _messages;

        public CapturingLogger(ConcurrentQueue<string> messages) => _messages = messages;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _messages.Enqueue(formatter(state, exception));
    }
}
