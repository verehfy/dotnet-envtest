using Microsoft.Extensions.Logging;

namespace Kubernetes.EnvTest.Tool;

/// <summary>
/// A minimal <see cref="ILogger"/> writing to standard error, so the tool's
/// standard output stays machine-readable (paths, env exports) while remaining
/// Native AOT friendly without pulling in the console logging provider.
/// </summary>
internal sealed class StderrLogger<T> : ILogger<T>
{
    private readonly LogLevel _minimumLevel;

    internal StderrLogger(LogLevel minimumLevel)
    {
        _minimumLevel = minimumLevel;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimumLevel;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        Console.Error.WriteLine($"{formatter(state, exception)}");
        if (exception is not null)
        {
            Console.Error.WriteLine(exception.ToString());
        }
    }
}
