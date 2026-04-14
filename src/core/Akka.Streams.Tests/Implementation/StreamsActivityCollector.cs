//-----------------------------------------------------------------------
// <copyright file="StreamsActivityCollector.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Akka.Streams.Implementation;

namespace Akka.Streams.Tests.Implementation
{
    /// <summary>
    /// Captures every Activity started on the "Akka.Streams" source while subscribed, so tests
    /// across the StreamsDiagnosticsSpec family can assert parent/child relationships,
    /// ActivityLinks for fan-in, etc.
    /// </summary>
    internal sealed class StreamsActivityCollector : IDisposable
    {
        static StreamsActivityCollector()
        {
            // On .NET Framework 4.8 the default Activity.DefaultIdFormat is Hierarchical.
            // With that default, Activity.Current?.Context comes back as default(ActivityContext)
            // (all-zero TraceId / SpanId) because Hierarchical activities don't populate the
            // W3C context struct. That makes every cross-span TraceId comparison in these
            // tests fail — not because the framework's context-carry logic is wrong, but
            // because there is no valid context to carry. Forcing W3C format at the test
            // process level makes the netfx test runner behave the same as the modern
            // runtimes where W3C is the default. This is a test-only concern: the
            // Akka.Streams library code works correctly under either format.
            Activity.DefaultIdFormat = ActivityIdFormat.W3C;
            Activity.ForceDefaultIdFormat = true;
        }

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
    }

    /// <summary>
    /// Convenience wrapper for attaching an <see cref="ActivityListener"/> to a producer-side
    /// <see cref="ActivitySource"/> used by tests to simulate upstream trace context. Keeping
    /// this in one place removes the 8-line listener boilerplate from every spec.
    /// </summary>
    internal sealed class ProducerActivityScope : IDisposable
    {
        static ProducerActivityScope()
        {
            // Same reason as StreamsActivityCollector.cctor — ensure W3C IDs on .NET Framework.
            // Duplicate-setting is a no-op, so we don't care which type's static ctor runs first.
            Activity.DefaultIdFormat = ActivityIdFormat.W3C;
            Activity.ForceDefaultIdFormat = true;
        }

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
