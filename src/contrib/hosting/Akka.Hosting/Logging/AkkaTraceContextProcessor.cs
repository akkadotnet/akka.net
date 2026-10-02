// -----------------------------------------------------------------------
//  <copyright file="AkkaTraceContextProcessor.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Reflection;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Akka.Hosting.Logging
{
    /// <summary>
    /// OpenTelemetry log processor that extracts trace context from Akka.NET log events
    /// and applies it to the <see cref="LogRecord"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This processor solves the problem that <see cref="Activity.Current"/> doesn't flow
    /// across actor mailbox boundaries because it uses <see cref="System.Threading.AsyncLocal{T}"/>.
    /// </para>
    /// <para>
    /// When <see cref="LoggerFactoryLogger"/> emits logs, it includes the original
    /// <see cref="ActivityContext"/> captured at log creation time as attributes
    /// (Akka.TraceId, Akka.SpanId, Akka.TraceFlags). This processor extracts those
    /// attributes and sets them on the <see cref="LogRecord"/> so that the log
    /// is properly correlated with the originating trace.
    /// </para>
    /// </remarks>
    public sealed class AkkaTraceContextProcessor : BaseProcessor<LogRecord>
    {
        /// <inheritdoc />
        public override void OnEnd(LogRecord data)
        {
            // Skip if TraceId is already set (e.g., from Activity.Current that happened to be present)
            if (data.TraceId != default)
            {
                return;
            }

            // Try to extract trace context from Akka log state attributes
            var attributes = data.Attributes;
            if (attributes == null)
            {
                return;
            }

            ActivityTraceId? traceId = null;
            ActivitySpanId? spanId = null;
            ActivityTraceFlags traceFlags = ActivityTraceFlags.None;

            foreach (var attr in attributes)
            {
                switch (attr.Key)
                {
                    case AkkaLogState.TraceIdKey:
                        traceId = ExtractTraceId(attr.Value);
                        break;

                    case AkkaLogState.SpanIdKey:
                        spanId = ExtractSpanId(attr.Value);
                        break;

                    case AkkaLogState.TraceFlagsKey when attr.Value is int flagsInt:
                        traceFlags = (ActivityTraceFlags)flagsInt;
                        break;
                }
            }

            // Only set trace context if we found both TraceId and SpanId
            if (traceId.HasValue && spanId.HasValue)
            {
                SetTraceContext(data, traceId.Value, spanId.Value, traceFlags);
            }
        }

        private static ActivityTraceId? ExtractTraceId(object? value)
        {
            // Handle ActivityTraceId directly (no allocation path)
            if (value is ActivityTraceId traceId)
            {
                return traceId;
            }

            // Fallback: handle string representation (for backwards compatibility)
            if (value is string traceIdStr)
            {
                return TryParseTraceId(traceIdStr);
            }

            return null;
        }

        private static ActivitySpanId? ExtractSpanId(object? value)
        {
            // Handle ActivitySpanId directly (no allocation path)
            if (value is ActivitySpanId spanId)
            {
                return spanId;
            }

            // Fallback: handle string representation (for backwards compatibility)
            if (value is string spanIdStr)
            {
                return TryParseSpanId(spanIdStr);
            }

            return null;
        }

        // LogRecord's TraceId, SpanId and TraceFlags setters are internal, so they are set through
        // reflection. The property lookups happen once. If a SetValue call ever fails (an OpenTelemetry
        // release changing those setters), we stop trying for the rest of the process rather than pay
        // for a failing reflection call on every log record. The trace context attributes remain in
        // the log state either way, so nothing is lost except the strongly typed fields.
        private static readonly PropertyInfo? TraceIdProperty = typeof(LogRecord).GetProperty("TraceId");
        private static readonly PropertyInfo? SpanIdProperty = typeof(LogRecord).GetProperty("SpanId");
        private static readonly PropertyInfo? TraceFlagsProperty = typeof(LogRecord).GetProperty("TraceFlags");
        private static volatile bool _traceContextSettersUnavailable;

        private static void SetTraceContext(LogRecord record, ActivityTraceId traceId, ActivitySpanId spanId, ActivityTraceFlags traceFlags)
        {
            if (_traceContextSettersUnavailable)
                return;

            try
            {
                TraceIdProperty?.SetValue(record, traceId);
                SpanIdProperty?.SetValue(record, spanId);
                TraceFlagsProperty?.SetValue(record, traceFlags);
            }
            catch (Exception ex) when (ex is TargetInvocationException or TargetException or ArgumentException or MethodAccessException)
            {
                _traceContextSettersUnavailable = true;
            }
        }

        private static ActivityTraceId? TryParseTraceId(string traceIdStr)
        {
            // ActivityTraceId.CreateFromString requires exactly 32 lowercase hex characters and throws
            // on anything else, so validate first instead of catching.
            return IsLowercaseHex(traceIdStr.AsSpan(), 32)
                ? ActivityTraceId.CreateFromString(traceIdStr.AsSpan())
                : null;
        }

        private static ActivitySpanId? TryParseSpanId(string spanIdStr)
        {
            // ActivitySpanId.CreateFromString requires exactly 16 lowercase hex characters.
            return IsLowercaseHex(spanIdStr.AsSpan(), 16)
                ? ActivitySpanId.CreateFromString(spanIdStr.AsSpan())
                : null;
        }

        private static bool IsLowercaseHex(ReadOnlySpan<char> value, int expectedLength)
        {
            if (value.Length != expectedLength)
                return false;

            foreach (var c in value)
            {
                if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                    return false;
            }

            return true;
        }
    }
}
