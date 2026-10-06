//-----------------------------------------------------------------------
// <copyright file="CanaryLoggerProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Akka.Maui.AOT.App;

/// <summary>
/// The MAUI canary's teeth, the same role <c>Akka.Hosting.AOT.App/WatchdogLoggerProvider.cs</c> plays for the
/// Hosting canary. It is the app's only <see cref="ILoggerProvider"/>, so it sees Akka's log events (through
/// <c>LoggerFactoryLogger</c>) and MAUI's own, including binding failures, which MAUI logs at Warning. It prints
/// every line, keeps every Warning and Error, and a self-test run fails if it kept any.
/// </summary>
internal sealed class CanaryLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _problems = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _markers = new();

    public ILogger CreateLogger(string categoryName) => new CanaryLogger(this, categoryName);

    public void Dispose()
    {
    }

    /// <summary>
    /// Returns a task that completes when an Information line containing <paramref name="marker"/> arrives. Call it
    /// before logging the marker. Akka's logger actor delivers in order, so once Akka's marker is seen, every Akka
    /// warning logged before it has been seen too.
    /// </summary>
    public Task ExpectMarker(string marker) =>
        _markers.GetOrAdd(marker, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    /// <summary>Fails the run if anything was logged at Warning or above, naming every message collected so far.</summary>
    public void ThrowIfAnyProblems(string phase)
    {
        if (_problems.IsEmpty)
            return;

        var problems = string.Join(Environment.NewLine + "  ", _problems);
        throw new InvalidOperationException(
            $"{_problems.Count} warning(s)/error(s) logged during {phase}:{Environment.NewLine}  {problems}");
    }

    private void Log(LogLevel level, string category, string message, Exception? exception)
    {
        var line = $"[{level}][{category}] {message}" + (exception is null ? "" : $" ({exception.GetType().Name}: {exception.Message})");
        Console.WriteLine($"{CanaryRun.Prefix} log {line}");

        if (level >= LogLevel.Warning)
        {
            _problems.Enqueue(line);
            return;
        }

        foreach (var (marker, seen) in _markers)
        {
            if (message.Contains(marker, StringComparison.Ordinal))
                seen.TrySetResult();
        }
    }

    private sealed class CanaryLogger(CanaryLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            provider.Log(logLevel, categoryName, formatter(state, exception), exception);
        }
    }
}
