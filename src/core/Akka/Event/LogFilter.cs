//-----------------------------------------------------------------------
// <copyright file="LogFilter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Akka.Actor;
using Akka.Actor.Setup;

namespace Akka.Event;

/*
 * NOTE: We do not do caching here - the number of log sources can be very large and
 * can change rapidly over the course of an application's lifecycle.
 *
 * This is out of band processing anyway, not on the fast past - exclude the logs
 * entirely if you care about performance here. This is for debugging and diagnostics.
 */

public enum LogFilterType
{
    /// <summary>
    /// Filter log messages based on their source
    /// </summary>
    Source,

    /// <summary>
    /// Filter log messages based on their content: message, exception, etc.
    /// </summary>
    /// <remarks>
    /// This is the slowest filter type, as it requires fully expanding the log message.
    /// </remarks>
    Content
}

public enum LogFilterDecision
{
    Keep,
    Drop,

    /// <summary>
    /// If we're asked to evaluate a filter and we don't have enough information to make a decision.
    ///
    /// For instance: a <see cref="LogFilterType.Source"/> stage gets asked to evaluate a message body.
    /// </summary>
    NoDecision
}

// <LogFilterBase>
/// <summary>
/// Base class for all log filters
/// </summary>
/// <remarks>
/// Worth noting: these run inside the Logging actors, so they're out of band
/// from any high performance workloads already.
///
/// In addition to this - all log filters will only run if the log level is enabled.
///
/// i.e. if we're at INFO level and the filter is set to a lower level, i.e. filtering DEBUG
/// logs, the filter won't even run.
/// </remarks>
public abstract class LogFilterBase : INoSerializationVerificationNeeded, IDeadLetterSuppression
{
    /// <summary>
    /// Which part of the log message this filter is evaluating?
    /// </summary>
    /// <remarks>
    /// This actually has a performance implication - if we're filtering on the source, which
    /// is already fully "expanded" into its final string representation, we can try to fail fast
    /// on that without any additional allocations.
    ///
    /// If we're filtering on the message, we have to fully expand the log message first which
    /// involves allocations. Users on really tight performance budgets should be aware of this.
    /// </remarks>
    public abstract LogFilterType FilterType { get; }

    /// <summary>
    /// Fast path designed to avoid allocating strings if we're filtering on the message content.
    /// </summary>
    /// <param name="content">Usually the fully expanded message content.</param>
    /// <param name="expandedMessage">The fully expanded message, optional.</param>
    public abstract LogFilterDecision ShouldKeepMessage(LogEvent content, string? expandedMessage = null);
}
// </LogFilterBase>

/// <summary>
/// Uses a regular expression to filter log messages based on their source.
/// </summary>
public sealed class RegexLogSourceFilter : LogFilterBase
{
    private readonly Regex _sourceRegex;

    public RegexLogSourceFilter(Regex sourceRegex)
    {
        _sourceRegex = sourceRegex;
    }

    public override LogFilterType FilterType => LogFilterType.Source;

    public override LogFilterDecision ShouldKeepMessage(LogEvent content, string? expandedMessage = null)
    {
        return _sourceRegex.IsMatch(content.LogSource) ? LogFilterDecision.Drop : LogFilterDecision.Keep;
    }
}

public sealed class ExactMatchLogSourceFilter : LogFilterBase
{
    private readonly string _source;
    private readonly StringComparison _comparison;

    public ExactMatchLogSourceFilter(string source, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        _source = source;
        _comparison = comparison;
    }

    public override LogFilterType FilterType => LogFilterType.Source;

    public override LogFilterDecision ShouldKeepMessage(LogEvent content,
        string? expandedMessage = null)
    {
        return content.LogSource == _source ? LogFilterDecision.Drop : LogFilterDecision.Keep;
    }
}

public sealed class RegexLogMessageFilter : LogFilterBase
{
    private readonly Regex _messageRegex;

    public RegexLogMessageFilter(Regex messageRegex)
    {
        _messageRegex = messageRegex;
    }

    public override LogFilterType FilterType => LogFilterType.Content;

    public override LogFilterDecision ShouldKeepMessage(LogEvent content,
        string? expandedMessage = null)
    {
        if(expandedMessage is not null)
            return _messageRegex.IsMatch(expandedMessage ?? string.Empty)
                ? LogFilterDecision.Drop
                : LogFilterDecision.Keep;
        
        return LogFilterDecision.NoDecision;
    }
}

/// <summary>
/// Runs inside the logging actor and evaluates if a log message should be kept.
/// </summary>
public class LogFilterEvaluator
{
    public static readonly LogFilterEvaluator NoFilters = EmptyLogFilterEvaluator.Instance;

    private readonly LogFilterBase[] _filters;


    /// <summary>
    /// "Fast path" indicator - if this is true, we only evaluate log sources and not the message content.
    /// </summary>
    public bool EvaluatesLogSourcesOnly { get; }

    public LogFilterEvaluator(LogFilterBase[] filters)
    {
        _filters = filters;
        EvaluatesLogSourcesOnly = filters.All(x => x.FilterType == LogFilterType.Source);
    }

    public virtual bool ShouldTryKeepMessage(LogEvent evt, out string expandedLogMessage)
    {
        expandedLogMessage = string.Empty;

        // fast and slow paths available here
        if (EvaluatesLogSourcesOnly)
        {
            foreach (var filter in _filters)
            {
                // saves on allocations in negative cases, where we can avoid expanding the message
                if (filter.ShouldKeepMessage(evt) == LogFilterDecision.Drop)
                    return false;
            }
        }
        else
        {
            // allocate the message just once
            var nullCheck = evt.ToString();

            if (nullCheck == null)
                return false; // no message to filter

            expandedLogMessage = nullCheck;

            foreach (var filter in _filters)
            {
                if (filter.ShouldKeepMessage(evt, expandedLogMessage) == LogFilterDecision.Drop)
                    return false;
            }
        }

        // expand the message if we haven't already
        // NOTE: might result in duplicate allocations in third party logging libraries. They'll have to adjust their
        // code accordingly after this feature ships.
        expandedLogMessage = (string.IsNullOrEmpty(expandedLogMessage) ? evt.Message.ToString() : expandedLogMessage)!;
        return true;
    }

    /// <summary>
    /// INTERNAL API - used to prevent unnecessary iterations when no filters are present
    /// </summary>
    private class EmptyLogFilterEvaluator : LogFilterEvaluator
    {
        public static readonly EmptyLogFilterEvaluator Instance = new();

        private EmptyLogFilterEvaluator() : base(Array.Empty<LogFilterBase>())
        {
        }

        public override bool ShouldTryKeepMessage(LogEvent evt, out string expandedLogMessage)
        {
            expandedLogMessage = evt.ToString()!;
            return true;
        }
    }
}

/// <summary>
/// Used to specify filters that can be used to curtail noise from sources in the Akka.NET log stream.
/// </summary>
public sealed class LogFilterSetup : Setup
{
    public LogFilterBase[] Filters { get; }

    public LogFilterEvaluator CreateEvaluator() => new(Filters);

    public LogFilterSetup(LogFilterBase[] filters)
    {
        Filters = filters;
    }
}

/// <summary>
/// Can be used to build a set of log filters to be used in conjunction with the <see cref="LogFilterSetup"/>.
/// </summary>
public sealed class LogFilterBuilder
{
    private readonly List<LogFilterBase> _filters = new();

    public LogFilterBuilder ExcludeSourceExactly(string source,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        _filters.Add(new ExactMatchLogSourceFilter(source, comparison));
        return this;
    }

    public LogFilterBuilder ExcludeSourceStartingWith(string sourceStart)
    {
        _filters.Add(new RegexLogSourceFilter(new Regex($"^{Regex.Escape(sourceStart)}", RegexOptions.Compiled)));
        return this;
    }

    public LogFilterBuilder ExcludeSourceContaining(string sourcePart)
    {
        _filters.Add(new RegexLogSourceFilter(new Regex(Regex.Escape(sourcePart), RegexOptions.Compiled)));
        return this;
    }

    public LogFilterBuilder ExcludeSourceEndingWith(string sourceEnd)
    {
        _filters.Add(new RegexLogSourceFilter(new Regex($"{Regex.Escape(sourceEnd)}$", RegexOptions.Compiled)));
        return this;
    }

    /// <summary>
    /// Performance boost: use your own pre-compiled Regex instance to filter log sources.
    /// </summary>
    public LogFilterBuilder ExcludeSourceRegex(Regex regex)
    {
        _filters.Add(new RegexLogSourceFilter(regex));
        return this;
    }

    /// <summary>
    /// Performance boost: use your own pre-compiled Regex instance to filter log messages.
    /// </summary>
    public LogFilterBuilder ExcludeMessageRegex(Regex regex)
    {
        _filters.Add(new RegexLogMessageFilter(regex));
        return this;
    }

    public LogFilterBuilder ExcludeMessageContaining(string messagePart)
    {
        _filters.Add(new RegexLogMessageFilter(new Regex(Regex.Escape(messagePart), RegexOptions.Compiled)));
        return this;
    }

    public LogFilterBuilder Add(LogFilterBase filter)
    {
        _filters.Add(filter);
        return this;
    }

    public LogFilterBuilder AddRange(IEnumerable<LogFilterBase> filters)
    {
        _filters.AddRange(filters);
        return this;
    }

    public LogFilterSetup Build()
    {
        return new LogFilterSetup(_filters.ToArray());
    }
}
