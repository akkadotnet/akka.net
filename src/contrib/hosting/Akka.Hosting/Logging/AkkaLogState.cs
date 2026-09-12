// -----------------------------------------------------------------------
//  <copyright file="AkkaLogState.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace Akka.Hosting.Logging
{
    /// <summary>
    /// Log state struct that carries both structured log properties and OpenTelemetry trace context.
    /// Implements <see cref="IEnumerable{T}"/> for compatibility with Microsoft.Extensions.Logging.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This struct is designed for minimal allocations - it holds references to existing data
    /// and yields values lazily via the enumerator rather than copying into a new collection.
    /// </para>
    /// <para>
    /// This struct is used to propagate trace context (TraceId, SpanId, TraceFlags) from the
    /// originating actor thread to the logging infrastructure, solving the problem that
    /// <see cref="Activity.Current"/> doesn't flow across actor mailbox boundaries because
    /// it uses <see cref="AsyncLocal{T}"/>.
    /// </para>
    /// </remarks>
    internal readonly struct AkkaLogState : IEnumerable<KeyValuePair<string, object?>>
    {
        /// <summary>
        /// Key for the trace ID in the log state dictionary.
        /// </summary>
        public const string TraceIdKey = "Akka.TraceId";

        /// <summary>
        /// Key for the span ID in the log state dictionary.
        /// </summary>
        public const string SpanIdKey = "Akka.SpanId";

        /// <summary>
        /// Key for the trace flags in the log state dictionary.
        /// </summary>
        public const string TraceFlagsKey = "Akka.TraceFlags";

        private readonly ActivityContext _activityContext;
        private readonly IReadOnlyDictionary<string, object>? _semanticProperties;
        private readonly string _actorPath;
        private readonly DateTimeOffset _timestamp;
        private readonly int _threadId;
        private readonly string _logSource;
        private readonly string _template;
        private readonly string _formattedMessage;

        /// <summary>
        /// Creates an <see cref="AkkaLogState"/> with trace context, semantic properties, and Akka metadata.
        /// </summary>
        /// <param name="activityContext">The activity context containing trace correlation IDs.</param>
        /// <param name="semanticProperties">Structured properties from the log message template (referenced, not copied).</param>
        /// <param name="actorPath">The path of the actor that generated the log.</param>
        /// <param name="timestamp">The timestamp when the log was created.</param>
        /// <param name="threadId">The managed thread ID of the originating thread.</param>
        /// <param name="logSource">The source of the log event.</param>
        /// <param name="template">The message template string.</param>
        /// <param name="formattedMessage">The pre-formatted message for display.</param>
        public AkkaLogState(
            ActivityContext activityContext,
            IReadOnlyDictionary<string, object> semanticProperties,
            string actorPath,
            DateTimeOffset timestamp,
            int threadId,
            string logSource,
            string template,
            string formattedMessage)
        {
            _activityContext = activityContext;
            _semanticProperties = semanticProperties;
            _actorPath = actorPath;
            _timestamp = timestamp;
            _threadId = threadId;
            _logSource = logSource;
            _template = template;
            _formattedMessage = formattedMessage;
        }

        /// <summary>
        /// Creates an <see cref="AkkaLogState"/> with trace context and Akka metadata for non-structured messages.
        /// </summary>
        /// <param name="activityContext">The activity context containing trace correlation IDs.</param>
        /// <param name="actorPath">The path of the actor that generated the log.</param>
        /// <param name="timestamp">The timestamp when the log was created.</param>
        /// <param name="threadId">The managed thread ID of the originating thread.</param>
        /// <param name="logSource">The source of the log event.</param>
        /// <param name="template">The message template string (for {OriginalFormat}).</param>
        /// <param name="formattedMessage">The pre-formatted message for display.</param>
        public AkkaLogState(
            ActivityContext activityContext,
            string actorPath,
            DateTimeOffset timestamp,
            int threadId,
            string logSource,
            string template,
            string formattedMessage)
        {
            _activityContext = activityContext;
            _semanticProperties = null;
            _actorPath = actorPath;
            _timestamp = timestamp;
            _threadId = threadId;
            _logSource = logSource;
            _template = template;
            _formattedMessage = formattedMessage;
        }

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            // Yield trace context if present (store structs directly, no ToString() allocation)
            if (_activityContext.TraceId != default)
            {
                yield return new KeyValuePair<string, object?>(TraceIdKey, _activityContext.TraceId);
                yield return new KeyValuePair<string, object?>(SpanIdKey, _activityContext.SpanId);
                yield return new KeyValuePair<string, object?>(TraceFlagsKey, (int)_activityContext.TraceFlags);
            }

            // Yield semantic properties if present (referenced, not copied)
            if (_semanticProperties != null)
            {
                foreach (var prop in _semanticProperties)
                {
                    yield return new KeyValuePair<string, object?>(prop.Key, prop.Value);
                }
            }

            // Yield Akka metadata
            yield return new KeyValuePair<string, object?>("ActorPath", _actorPath);
            yield return new KeyValuePair<string, object?>("Timestamp", _timestamp);
            yield return new KeyValuePair<string, object?>("Thread", _threadId);
            yield return new KeyValuePair<string, object?>("LogSource", _logSource);

            // Yield OriginalFormat for MEL convention
            yield return new KeyValuePair<string, object?>("{OriginalFormat}", _template);
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc />
        public override string ToString() => _formattedMessage;
    }
}
