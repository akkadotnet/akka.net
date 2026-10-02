//-----------------------------------------------------------------------
// <copyright file="StreamsScenarios.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Actors;
using Akka.Streams.Dsl;

namespace Akka.Hosting.AOT.App;

/// <summary>
/// Akka.Streams graphs that cross island boundaries (#8731). Each boundary used to be built with
/// MakeGenericType + Activator, which Native AOT trims away. Every scenario runs once with int and
/// once with string, because value and reference types get different compiled instantiations.
/// No TrimmerRootDescriptor is involved: if a boundary type is still built reflectively, its
/// constructor is gone and the run fails with MissingMethodException.
/// </summary>
internal static class StreamsScenarios
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static async Task RunAsync(ActorSystem system)
    {
        var mat = system.Materializer(namePrefix: "aot-canary");

        // .Async() splits the graph into islands: BoundarySubscriber, BoundaryPublisher and
        // ActorOutputBoundary on every edge between them.
        await ExpectAsync(
            Source.From(Enumerable.Range(1, 10)).Async().Select(x => x * 2).Async().RunWith(Sink.Seq<int>(), mat),
            Enumerable.Range(1, 10).Select(x => x * 2), "async boundaries (int)");
        await ExpectAsync(
            Source.From(Enumerable.Range(1, 10)).Select(x => x.ToString()).Async().Select(s => s + "!").Async()
                .RunWith(Sink.Seq<string>(), mat),
            Enumerable.Range(1, 10).Select(x => x + "!"), "async boundaries (string)");

        // Sink.AsPublisher(false) is a sink module that hands back a plain ISubscriber<T>, and
        // Source.FromPublisher is a source module - both are wired up by MaterializerSession.
        await ExpectAsync(
            Source.FromPublisher(Source.From(Enumerable.Range(1, 5)).RunWith(Sink.AsPublisher<int>(false), mat))
                .RunWith(Sink.Seq<int>(), mat),
            Enumerable.Range(1, 5), "AsPublisher -> FromPublisher (int)");
        await ExpectAsync(
            Source.FromPublisher(Source.From(new[] { "a", "b", "c" }).RunWith(Sink.AsPublisher<string>(false), mat))
                .RunWith(Sink.Seq<string>(), mat),
            new[] { "a", "b", "c" }, "AsPublisher -> FromPublisher (string)");

        // Sink.AsPublisher(true) runs a fan-out processor actor whose output buffer type is built with
        // Activator - this proves the [DynamicallyAccessedMembers] on SubscriberManagement's TStreamBuffer.
        await FanOutAsync(mat, Enumerable.Range(1, 5).ToArray(), "int");
        await FanOutAsync(mat, new[] { "a", "b", "c" }, "string");

        // Source.ActorPublisher is what the persistence query read journals are built on.
        await ExpectAsync(
            Source.ActorPublisher<int>(Props.Create(() => new CountingPublisher<int>(5, i => i)))
                .RunWith(Sink.Seq<int>(), mat),
            Enumerable.Range(1, 5), "Source.ActorPublisher (int)");
        await ExpectAsync(
            Source.ActorPublisher<string>(Props.Create(() => new CountingPublisher<string>(3, i => "e" + i)))
                .RunWith(Sink.Seq<string>(), mat),
            new[] { "e1", "e2", "e3" }, "Source.ActorPublisher (string)");

        // A graph that reads its own materialized value back as a stream element: auto-fusing rebuilds
        // the MaterializedValueSource<T> it finds inside.
        await ReadOwnMaterializedValueAsync(mat, 42);
        await ReadOwnMaterializedValueAsync(mat, "forty-two");
    }

    private static Task FanOutAsync<T>(IMaterializer mat, T[] elements, string typeName)
        => ExpectAsync(
            Source.FromPublisher(Source.From(elements).RunWith(Sink.AsPublisher<T>(true), mat))
                .RunWith(Sink.Seq<T>(), mat),
            elements, $"fan-out AsPublisher ({typeName})");

    private static async Task ReadOwnMaterializedValueAsync<T>(IMaterializer mat, T value)
    {
        var seen = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var matValue = RunnableGraph.FromGraph(GraphDsl.Create(
            Source.Single(value).MapMaterializedValue(_ => value), (b, single) =>
            {
                b.From(single).To(Sink.Ignore<T>().MapMaterializedValue(_ => NotUsed.Instance));
                b.From(b.MaterializedValue).To(Sink.ForEach<T>(x => seen.TrySetResult(x)).MapMaterializedValue(_ => NotUsed.Instance));
                return ClosedShape.Instance;
            })).Run(mat);
        var echoed = await seen.Task.WaitAsync(Timeout);
        if (!Equals(matValue, value) || !Equals(echoed, value))
            throw new InvalidOperationException($"materialized value graph gave {matValue}/{echoed}, expected {value}/{value}");
    }

    private static async Task ExpectAsync<T>(Task<IImmutableList<T>> run, IEnumerable<T> expected, string scenario)
    {
        var actual = await run.WaitAsync(Timeout);
        if (!actual.SequenceEqual(expected))
            throw new InvalidOperationException(
                $"streams scenario '{scenario}' produced [{string.Join(", ", actual)}], expected [{string.Join(", ", expected)}]");
    }

    /// <summary>
    /// Emits <c>1..count</c> (mapped to <typeparamref name="T"/>) as demand arrives, then completes.
    /// </summary>
    private sealed class CountingPublisher<T> : ActorPublisher<T>
    {
        private readonly int _count;
        private readonly Func<int, T> _map;
        private int _next = 1;

        public CountingPublisher(int count, Func<int, T> map)
        {
            _count = count;
            _map = map;
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case Request:
                    while (TotalDemand > 0 && _next <= _count)
                        OnNext(_map(_next++));
                    if (_next > _count)
                        OnCompleteThenStop();
                    return true;
                case Cancel:
                    Context.Stop(Self);
                    return true;
                default:
                    return false;
            }
        }
    }
}
