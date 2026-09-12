// -----------------------------------------------------------------------
//  <copyright file="AkkaTraceContextProcessor.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Diagnostics;
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
    /// across actor mailbox boundaries because it uses <see cref="AsyncLocal{T}"/>.
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

        private static void SetTraceContext(LogRecord record, ActivityTraceId traceId, ActivitySpanId spanId, ActivityTraceFlags traceFlags)
        {
            // LogRecord has internal setters, so we need to use reflection
            try
            {
                var recordType = typeof(LogRecord);

                var traceIdProp = recordType.GetProperty("TraceId");
                var spanIdProp = recordType.GetProperty("SpanId");
                var traceFlagsProp = recordType.GetProperty("TraceFlags");

                traceIdProp?.SetValue(record, traceId);
                spanIdProp?.SetValue(record, spanId);
                traceFlagsProp?.SetValue(record, traceFlags);
            }
            catch
            {
                // Silently ignore reflection failures
                // The trace context attributes are still present in the log state
            }
        }

        private static ActivityTraceId? TryParseTraceId(string traceIdStr)
        {
            try
            {
                // ActivityTraceId expects a 32 character hex string
                if (traceIdStr.Length == 32)
                {
                    return ActivityTraceId.CreateFromString(traceIdStr.AsSpan());
                }
            }
            catch
            {
                // Ignore parse failures
            }
            return null;
        }

        private static ActivitySpanId? TryParseSpanId(string spanIdStr)
        {
            try
            {
                // ActivitySpanId expects a 16 character hex string
                if (spanIdStr.Length == 16)
                {
                    return ActivitySpanId.CreateFromString(spanIdStr.AsSpan());
                }
            }
            catch
            {
                // Ignore parse failures
            }
            return null;
        }
    }
}
