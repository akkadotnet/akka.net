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
/// MakeGenericType + Activator, which Native AOT trims away. Every scenario runs once with a value
/// type and once with a reference type, because the two get different compiled instantiations.
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

        // Sink.AsPublisher(true) runs a fan-out processor actor, which hands out ActorSubscriptions.
        await ExpectAsync(
            Source.FromPublisher(Source.From(Enumerable.Range(1, 5)).RunWith(Sink.AsPublisher<int>(true), mat))
                .RunWith(Sink.Seq<int>(), mat),
            Enumerable.Range(1, 5), "fan-out AsPublisher (int)");

        // Source.ActorPublisher is what the persistence query read journals are built on.
        await ExpectAsync(
            Source.ActorPublisher<int>(Props.Create(() => new CountingPublisher<int>(5, i => i)))
                .RunWith(Sink.Seq<int>(), mat),
            Enumerable.Range(1, 5), "Source.ActorPublisher (int)");
        await ExpectAsync(
            Source.ActorPublisher<string>(Props.Create(() => new CountingPublisher<string>(3, i => "e" + i)))
                .RunWith(Sink.Seq<string>(), mat),
            new[] { "e1", "e2", "e3" }, "Source.ActorPublisher (string)");

        // Source.Queue materializes a queue whose consumer sits behind an async boundary.
        var (queue, done) = Source.Queue<int>(16, OverflowStrategy.Backpressure)
            .Async()
            .ToMaterialized(Sink.Seq<int>(), Keep.Both)
            .Run(mat);
        foreach (var i in Enumerable.Range(1, 3))
            await queue.OfferAsync(i);
        queue.Complete();
        await ExpectAsync(done, new[] { 1, 2, 3 }, "Source.Queue (int)");

        // A graph that reads its own materialized value (an int) back as a stream element:
        // auto-fusing rebuilds the MaterializedValueSource it finds inside.
        var seen = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var matValue = RunnableGraph.FromGraph(GraphDsl.Create(
            Source.Single(1).MapMaterializedValue(_ => 42), (b, single) =>
            {
                b.From(single).To(Sink.Ignore<int>().MapMaterializedValue(_ => NotUsed.Instance));
                b.From(b.MaterializedValue).To(Sink.ForEach<int>(x => seen.TrySetResult(x)).MapMaterializedValue(_ => NotUsed.Instance));
                return ClosedShape.Instance;
            })).Run(mat);
        var echoed = await seen.Task.WaitAsync(Timeout);
        if (matValue != 42 || echoed != 42)
            throw new InvalidOperationException($"materialized value graph gave {matValue}/{echoed}, expected 42/42");
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
