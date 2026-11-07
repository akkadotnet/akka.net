//-----------------------------------------------------------------------
// <copyright file="WatchdogLoggerProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Akka.Hosting.AOT.App;

/// <summary>
/// The Hosting canary's teeth, playing the same role <c>Akka.AOT.App/LogWatchdogFilter.cs</c> plays
/// for the plain-core canary, but attached one layer higher: as an <see cref="ILoggerProvider"/> on
/// the generic host's own <see cref="ILoggerFactory"/> instead of an Akka <c>LogFilterBase</c>.
/// </summary>
/// <remarks>
/// Hosting's default logging path is Akka log event -> <c>LoggerFactoryLogger</c> (an Akka logger
/// actor) -> <c>ILogger&lt;ActorSystem&gt;</c> -> every registered <see cref="ILoggerProvider"/>. A
/// provider here therefore sees Akka's own WARNING/ERROR output *and* anything the host or
/// Microsoft.Extensions.* logs directly.
/// </remarks>
internal sealed class WatchdogLoggerProvider(string marker) : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _problems = new();
    private readonly TaskCompletionSource _markerSeen = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ILogger CreateLogger(string categoryName) => new WatchdogLogger(categoryName, _problems, _markerSeen, marker);

    public void Dispose()
    {
    }

    /// <summary>
    /// Waits for the Info-level marker line. Proves the logger actor actually delivered a log event
    /// through Microsoft.Extensions.Logging - and, since it delivers in order, that anything logged
    /// earlier has been seen too, which is what makes <see cref="ThrowIfAnyProblems"/> race-free right
    /// after this returns.
    /// </summary>
    public async Task WaitForMarkerAsync(TimeSpan timeout)
    {
        var winner = await Task.WhenAny(_markerSeen.Task, Task.Delay(timeout));
        if (winner != _markerSeen.Task)
            throw new InvalidOperationException(
                $"never saw the marker line '{marker}' within {timeout} - LoggerFactoryLogger did not route Akka's log events to Microsoft.Extensions.Logging");
    }

    /// <summary>Fails the run if anything was logged at Warning or above, naming every message collected so far.</summary>
    public void ThrowIfAnyProblems(string phase)
    {
        if (_problems.IsEmpty)
            return;

        var problems = string.Join(Environment.NewLine + "  ", _problems);
        throw new InvalidOperationException(
            $"{_problems.Count} warning(s)/error(s) logged during {phase}:{Environment.NewLine}  {problems}");
    }

    private sealed class WatchdogLogger(string categoryName, ConcurrentQueue<string> problems, TaskCompletionSource markerSeen, string marker) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // Information too, not just Warning/Error - that's the level the marker line above logs at.
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Information)
                return;

            var message = formatter(state, exception);

            if (logLevel < LogLevel.Warning)
            {
                if (message.Contains(marker))
                    markerSeen.TrySetResult();
                return;
            }

            problems.Enqueue($"[{logLevel}][{categoryName}] {message}" + (exception is null ? "" : $" ({exception.GetType().Name}: {exception.Message})"));

            // the run should still show what it is failing on
            Console.WriteLine($"[canary-hosting] {logLevel}: [{categoryName}] {message}");
        }
    }
}
