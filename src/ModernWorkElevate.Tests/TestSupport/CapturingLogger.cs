namespace ModernWorkElevate.Tests.TestSupport;

using Microsoft.Extensions.Logging;

/// <summary>
/// Records every log call with everything a sink could write: the rendered message,
/// each structured argument and the exception's <see cref="Exception.ToString"/>.
/// <see cref="AllText"/> joins them, so a test can assert that nothing reached the
/// log — in any form — that the log must not hold.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries => _entries;

    public string AllText => string.Join(Environment.NewLine, _entries);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var parts = new List<string> { formatter(state, exception) };
        if (state is IEnumerable<KeyValuePair<string, object?>> values)
        {
            parts.AddRange(values.Select(kv => $"{kv.Key}={kv.Value}"));
        }

        if (exception is not null)
        {
            parts.Add(exception.ToString());
        }

        _entries.Add(string.Join(" | ", parts));
    }
}
