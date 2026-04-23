//-----------------------------------------------------------------------
// <copyright file="StreamsActivityCollector.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Implementation;

namespace Akka.Streams.Tests.Implementation
{
    /// <summary>
    /// Captures every Activity started on the "Akka.Streams" source while subscribed, so tests
    /// across the StreamsDiagnosticsSpec family can assert parent/child relationships,
    /// ActivityLinks for fan-in, etc.
    /// </summary>
    /// <summary>
    /// Test process setup shared across the Streams*Spec family.
    /// </summary>
    internal static class StreamsActivityTestSetup
    {
        /// <summary>
        /// Force W3C Activity IDs at the test process level. On .NET Framework 4.8 the default
        /// <see cref="Activity.DefaultIdFormat"/> is <see cref="ActivityIdFormat.Hierarchical"/>,
        /// under which <c>Activity.Current?.Context</c> returns <c>default(ActivityContext)</c>
        /// (all-zero TraceId/SpanId) because Hierarchical activities don't populate the W3C
        /// context struct — every cross-span TraceId comparison in these tests would then fail.
        /// Modern runtimes default to W3C, so calling this once per test process makes the netfx
        /// runner behave the same as net6+/net10. Idempotent — safe to call from multiple
        /// static constructors. Test-only: the Akka.Streams library itself works under either
        /// format.
        /// </summary>
        internal static void EnsureW3CActivityFormat()
        {
            Activity.DefaultIdFormat = ActivityIdFormat.W3C;
            Activity.ForceDefaultIdFormat = true;
        }
    }

    internal sealed class StreamsActivityCollector : IDisposable
    {
        static StreamsActivityCollector() => StreamsActivityTestSetup.EnsureW3CActivityFormat();

        private readonly ActivityListener _listener;
        public ConcurrentQueue<Activity> StartedActivities { get; } = new();
        public ConcurrentQueue<Activity> StoppedActivities { get; } = new();

        public StreamsActivityCollector()
        {
            // Force StreamsDiagnostics type init before creating the listener, otherwise
            // AddActivityListener can reenter during its iteration over existing sources and
            // hit a partially-initialized static field.
            _ = StreamsDiagnostics.ActivitySource;

            var started = StartedActivities;
            var stopped = StoppedActivities;
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Akka.Streams",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = a => started.Enqueue(a),
                ActivityStopped = a => stopped.Enqueue(a)
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public void Dispose() => _listener.Dispose();

        public async Task WaitForSpansAsync(
            int atLeast,
            int timeoutSeconds = 5,
            Func<Activity[], bool> predicate = null)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                var snap = StoppedActivities.ToArray();
                if (snap.Length >= atLeast && (predicate == null || predicate(snap)))
                    return;
                await Task.Delay(25);
            }
        }

        public Task WaitForLinkedSpanAsync(int timeoutSeconds = 5)
            => WaitForSpansAsync(1, timeoutSeconds,
                snap => snap.Any(a => (a.Links?.Count() ?? 0) > 0));
    }

    /// <summary>
    /// Convenience wrapper for attaching an <see cref="ActivityListener"/> to a producer-side
    /// <see cref="ActivitySource"/> used by tests to simulate upstream trace context. Keeping
    /// this in one place removes the 8-line listener boilerplate from every spec.
    /// </summary>
    internal sealed class ProducerActivityScope : IDisposable
    {
        static ProducerActivityScope() => StreamsActivityTestSetup.EnsureW3CActivityFormat();

        public ActivitySource Source { get; }
        private readonly ActivityListener _listener;

        public ProducerActivityScope(string name)
        {
            Source = new ActivitySource(name);
            _listener = new ActivityListener
            {
                ShouldListenTo = src => src.Name == name,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = _ => { },
                ActivityStopped = _ => { }
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public Activity Start(string operationName) =>
            Source.StartActivity(operationName, ActivityKind.Internal);

        public void Dispose()
        {
            _listener.Dispose();
            Source.Dispose();
        }
    }
}
