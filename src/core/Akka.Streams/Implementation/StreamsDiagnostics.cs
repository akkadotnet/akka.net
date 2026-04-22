//-----------------------------------------------------------------------
// <copyright file="StreamsDiagnostics.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        internal const string OperationStage = "akka.stream.stage";
        internal const string OperationIngress = "akka.stream.ingress";
        internal const string OperationIngressQueued = "akka.stream.ingress.queued";
        internal const string TagStageType = "stream.stage.type";
        internal const string TagFanInLinks = "stream.fan_in.links";

        private static readonly string Version =
            typeof(StreamsDiagnostics).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        /// <summary>
        /// The framework-owned <see cref="ActivitySource"/>. Register via
        /// <c>.AddSource(StreamsDiagnostics.ActivitySourceName)</c> on your OTel <c>TracerProvider</c>.
        /// </summary>
        public static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

        // Caches stage name and pre-formatted operation names per concrete GraphStageLogic type,
        // so the hot path never does reflection or string interpolation after the first element.
        private static readonly ConcurrentDictionary<Type, StageNameEntry> StageNameCache = new();

        /// <summary>
        /// Returns a short, human-readable name for a stage logic suitable for use as an
        /// <see cref="Activity"/> operation name. Prefers the declaring outer stage type name
        /// (e.g. "Select") over the nested Logic class name, and strips generic-arity backticks.
        /// Results are cached per concrete type.
        /// </summary>
        public static string GetStageName(GraphStageLogic stage)
            => GetStageNameEntry(stage).StageName;

        internal static string GetStageOperationName(GraphStageLogic stage)
            => GetStageNameEntry(stage).StageOperationName;

        internal static string GetIngressOperationName(GraphStageLogic stage)
            => GetStageNameEntry(stage).IngressOperationName;

        private static StageNameEntry GetStageNameEntry(GraphStageLogic stage)
            => StageNameCache.GetOrAdd(stage.GetType(), static t =>
            {
                var outerType = t.DeclaringType ?? t;
                var name = outerType.Name;
                var tick = name.IndexOf('`');
                var stageName = tick > 0 ? name.Substring(0, tick) : name;
                return new StageNameEntry(stageName);
            });

        /// <summary>
        /// Emits fan-in trace context for a list of collected <see cref="ActivityContext"/>s:
        /// the first entry becomes the primary parent, the remainder become <see cref="ActivityLink"/>s.
        /// </summary>
        internal static void EmitFanInTraceContexts<T>(
            GraphStageLogic logic,
            Outlet<T> outlet,
            List<ActivityContext> contexts)
        {
            if (contexts == null || contexts.Count == 0) return;
            var primary = contexts[0];
            IReadOnlyList<ActivityContext> rest = contexts.Count > 1
                ? contexts.GetRange(1, contexts.Count - 1)
                : null;
            logic.SetFanInTraceContext(outlet, primary, rest);
        }

        private sealed class StageNameEntry
        {
            public string StageName { get; }
            public string StageOperationName { get; }
            public string IngressOperationName { get; }

            public StageNameEntry(string stageName)
            {
                StageName = stageName;
                StageOperationName = string.Concat(OperationStage, " ", stageName);
                IngressOperationName = string.Concat(OperationIngress, " ", stageName);
            }
        }
    }
}
