//-----------------------------------------------------------------------
// <copyright file="StreamsDiagnostics.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Reflection;
using Akka.Annotations;
using Akka.Streams.Stage;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Framework-owned <see cref="ActivitySource"/> used to emit per-stage spans when an element
    /// flowing through an Akka.Streams graph has a live parent trace context captured from the
    /// producer thread. Users enable these spans by registering the source with OpenTelemetry:
    /// <code>.AddSource("Akka.Streams")</code>.
    ///
    /// When the source has no listeners, <see cref="ActivitySource.StartActivity(string, ActivityKind)"/>
    /// returns <c>null</c> and the instrumentation path becomes a no-op — zero allocation when
    /// tracing is not in use.
    /// </summary>
    [InternalApi]
    public static class StreamsDiagnostics
    {
        /// <summary>
        /// The name of the <see cref="ActivitySource"/> used for Akka.Streams stage spans.
        /// </summary>
        public const string ActivitySourceName = "Akka.Streams";

        private static readonly string Version =
            typeof(StreamsDiagnostics).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        /// <summary>
        /// The framework-owned <see cref="ActivitySource"/>. Register via
        /// <c>.AddSource(StreamsDiagnostics.ActivitySourceName)</c> on your OTel <c>TracerProvider</c>.
        /// </summary>
        public static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

        /// <summary>
        /// Returns a short, human-readable name for a stage logic suitable for use as an
        /// <see cref="Activity"/> operation name. Prefers the declaring outer stage type name
        /// (e.g. "Select") over the nested Logic class name, and strips generic-arity backticks.
        /// </summary>
        public static string GetStageName(GraphStageLogic stage)
        {
            var logicType = stage.GetType();
            // GraphStageLogic is typically a nested class ("Logic") inside the outer stage
            // (e.g. Select<TIn,TOut>), so walking up DeclaringType gives us the user-facing name.
            var outerType = logicType.DeclaringType ?? logicType;
            var name = outerType.Name;
            var tick = name.IndexOf('`');
            return tick > 0 ? name.Substring(0, tick) : name;
        }
    }
}
