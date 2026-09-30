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
/// provider here therefore sees Akka's own WARNING/ERROR output (the same
/// "did not resolve to an actual Type" messages the core canary watches for) *and* anything the
/// host itself, ASP.NET Core-style middleware, or Microsoft.Extensions.* logs directly - both
/// surfaces the task asked this canary to watch.
/// </remarks>
internal sealed class WatchdogLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _problems = new();

    public ILogger CreateLogger(string categoryName) => new WatchdogLogger(categoryName, _problems);

    public void Dispose()
    {
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

    private sealed class WatchdogLogger(string categoryName, ConcurrentQueue<string> problems) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Warning)
                return;

            var message = formatter(state, exception);
            problems.Enqueue($"[{logLevel}][{categoryName}] {message}" + (exception is null ? "" : $" ({exception.GetType().Name}: {exception.Message})"));

            // the run should still show what it is failing on
            Console.WriteLine($"[canary-hosting] {logLevel}: [{categoryName}] {message}");
        }
    }
}
