// -----------------------------------------------------------------------
//  <copyright file="AkkaOpenTelemetryExtensions.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Hosting.Logging;
using OpenTelemetry.Logs;

namespace Akka.Hosting
{
    /// <summary>
    /// Extension methods for integrating Akka.NET logging with OpenTelemetry.
    /// </summary>
    public static class AkkaOpenTelemetryExtensions
    {
        /// <summary>
        /// Adds the Akka.NET trace correlation processor to the OpenTelemetry logging pipeline.
        /// </summary>
        /// <param name="options">The OpenTelemetry logger options.</param>
        /// <returns>The options instance for chaining.</returns>
        /// <remarks>
        /// <para>
        /// This processor extracts trace context (TraceId, SpanId, TraceFlags) from Akka.NET
        /// log events and applies them to OpenTelemetry <see cref="OpenTelemetry.Logs.LogRecord"/>
        /// instances. This enables proper trace correlation for logs emitted from actor code,
        /// solving the problem that <see cref="System.Diagnostics.Activity.Current"/> doesn't
        /// flow across actor mailbox boundaries.
        /// </para>
        /// <para>
        /// <b>Important:</b> This processor should be registered <b>before</b> any exporters
        /// to ensure the trace context is applied before logs are exported.
        /// </para>
        /// <example>
        /// <code>
        /// builder.Logging.AddOpenTelemetry(options =>
        /// {
        ///     options.SetResourceBuilder(ResourceBuilder.CreateDefault()
        ///         .AddService("my-service"));
        ///
        ///     // Register Akka trace correlation FIRST (before exporters)
        ///     options.AddAkkaTraceCorrelation();
        ///
        ///     options.AddOtlpExporter();
        /// });
        /// </code>
        /// </example>
        /// </remarks>
        public static OpenTelemetryLoggerOptions AddAkkaTraceCorrelation(this OpenTelemetryLoggerOptions options)
        {
            options.AddProcessor(new AkkaTraceContextProcessor());
            return options;
        }
    }
}
