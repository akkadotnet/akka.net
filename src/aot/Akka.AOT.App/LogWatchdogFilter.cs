//-----------------------------------------------------------------------
// <copyright file="LogWatchdogFilter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Concurrent;
using Akka.Event;

namespace Akka.AOT.App;

/// <summary>
/// A <see cref="LogFilterBase"/> that keeps every WARNING and ERROR the stdout logger is asked to print,
/// and never drops anything.
/// </summary>
/// <remarks>
/// This is the canary's teeth. <c>Serialization</c> and <c>Mailboxes</c> log-and-continue when a configured
/// type name does not resolve ("The type name for serializer 'json' did not resolve to an actual Type",
/// "Mailbox Requirement mapping [...] is not an actual type"), so an <see cref="Akka.Actor.ActorSystem"/>
/// can come up looking healthy with nothing registered. Turning those warnings into a failed run is what
/// stops the canary from going green on an empty boot.
/// </remarks>
internal sealed class LogWatchdogFilter : LogFilterBase
{
    private readonly ConcurrentQueue<string> _problems = new();

    public override LogFilterType FilterType => LogFilterType.Content;

    public override LogFilterDecision ShouldKeepMessage(LogEvent content, string? expandedMessage = null)
    {
        switch (content.LogLevel())
        {
            case LogLevel.WarningLevel:
            case LogLevel.ErrorLevel:
                _problems.Enqueue($"[{content.LogLevel()}][{content.LogSource}] {expandedMessage ?? content.Message?.ToString()}");
                break;
        }

        // the run should still show the output it is failing on
        return LogFilterDecision.Keep;
    }

    /// <summary>
    /// Fails the run if anything was logged at WARNING or above, naming every message collected so far.
    /// </summary>
    public void ThrowIfAnyProblems(string label, string phase)
    {
        if (_problems.IsEmpty)
            return;

        var problems = string.Join(Environment.NewLine + "  ", _problems);
        throw new InvalidOperationException(
            $"{label}: {_problems.Count} warning(s)/error(s) logged during {phase}:{Environment.NewLine}  {problems}");
    }
}
