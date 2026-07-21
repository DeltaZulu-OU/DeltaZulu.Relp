using Microsoft.Extensions.Logging;

namespace DeltaZulu.Forward;

internal sealed class ForwardLogger(string categoryName, ForwardLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider.ScopeProvider.Push(state);

    public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null)
        {
            return;
        }

        var scopes = new List<object?>();
        if (provider.Options.IncludeScopes)
        {
            provider.ScopeProvider.ForEachScope(static (scope, destination) => destination.Add(scope), scopes);
        }

        provider.Enqueue(new ForwardLogEntry(DateTimeOffset.UtcNow, categoryName, logLevel, eventId, message, exception, scopes));
    }
}
