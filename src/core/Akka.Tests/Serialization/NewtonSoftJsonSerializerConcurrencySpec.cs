//-----------------------------------------------------------------------
// <copyright file="NewtonSoftJsonSerializerConcurrencySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Serialization
{
    /// <summary>
    /// Defensive concurrency smoke test for <see cref="NewtonSoftJsonSerializer"/>.
    ///
    /// <see cref="NewtonSoftJsonSerializer.FromBinary(byte[], Type)"/> falls back to
    /// <c>JObject.ToObject(Type, JsonSerializer)</c> whenever Newtonsoft can't resolve a concrete type from the
    /// JSON directly (see the "TranslateSurrogate" fallback), and that path is exercised through a single shared
    /// <see cref="JsonSerializer"/> instance held for the lifetime of the <see cref="NewtonSoftJsonSerializer"/>.
    /// Newtonsoft's <c>PreserveReferencesHandling</c> (on by default in Akka) lazily tracks $id/$ref bookkeeping,
    /// so this spec hammers many threads through <c>ToBinary</c>/<c>FromBinary</c> on that one shared instance and
    /// asserts every thread's object graph round-trips correctly and in isolation from every other thread's -
    /// this is a defensive invariant check, not a reproduction of a specific historical bug: as of Newtonsoft
    /// 13.0.x, the $id/$ref reference-tracking table is scoped per top-level (de)serialize operation rather than
    /// mutated directly on the shared <see cref="JsonSerializer"/> instance, so this spec is expected to pass
    /// even without the thread-safety fixes made elsewhere in this change set.
    ///
    /// <c>EncodeTypeNames = false</c> is used here deliberately: without embedded "$type" metadata, the top-level
    /// <c>FromBinary</c> call is *guaranteed* to hit the JObject fallback for every single call (rather than only
    /// in rare/ambiguous cases), which is what lets this spec hammer the relevant code path directly and
    /// repeatably. <c>PreserveObjectReferences</c> is left at its default (true) since that's what makes the
    /// $id/$ref bookkeeping relevant in the first place.
    /// </summary>
    public class NewtonSoftJsonSerializerConcurrencySpec : AkkaSpec
    {
        public NewtonSoftJsonSerializerConcurrencySpec(ITestOutputHelper output) : base(output)
        {
        }

        public sealed class ConcurrencyTestNode
        {
            public string Name { get; set; }
            public int Value { get; set; }
        }

        public sealed class ConcurrencyTestMessage
        {
            public int ThreadId { get; set; }
            public int Iteration { get; set; }
            public ConcurrencyTestNode First { get; set; }
            public ConcurrencyTestNode Second { get; set; }
        }

        [Fact(DisplayName = "NewtonSoftJsonSerializer should produce correct, isolated round-trips for concurrent ToBinary/FromBinary calls that hit the JObject-fallback path with PreserveObjectReferences")]
        public async Task Should_round_trip_distinct_graphs_concurrently_without_corruption()
        {
            var settings = new NewtonSoftJsonSerializerSettings(
                encodeTypeNames: false,
                preserveObjectReferences: true,
                converters: Enumerable.Empty<Type>(),
                usePooledStringBuilder: true,
                stringBuilderMinSize: 2048,
                stringBuilderMaxSize: 32768);

            // ONE shared serializer instance across all threads - this is the crux of the defect being
            // guarded against: every thread calls ToBinary/FromBinary on the SAME NewtonSoftJsonSerializer.
            var serializer = new NewtonSoftJsonSerializer((ExtendedActorSystem)Sys, settings);

            // Floor at 4 so single-core CI agents still exercise real concurrency.
            var threadCount = Math.Max(4, Environment.ProcessorCount);
            const int iterations = 3000;

            var exceptions = new ConcurrentBag<Exception>();
            var mismatches = new ConcurrentBag<string>();

            var tasks = Enumerable.Range(0, threadCount).Select(t => Task.Run(() =>
            {
                for (var i = 0; i < iterations; i++)
                {
                    var tag = $"thread-{t}-iter-{i}";
                    var expectedValue = t * 1_000_000 + i;
                    var node = new ConcurrencyTestNode { Name = tag, Value = expectedValue };
                    var msg = new ConcurrencyTestMessage
                    {
                        ThreadId = t,
                        Iteration = i,
                        First = node,
                        Second = node // same reference twice - exercises $id/$ref bookkeeping within one graph
                    };

                    try
                    {
                        var bytes = serializer.ToBinary(msg);
                        var round = serializer.FromBinary<ConcurrencyTestMessage>(bytes);

                        if (round.ThreadId != t || round.Iteration != i)
                            mismatches.Add($"[{tag}] expected ThreadId/Iteration ({t},{i}), got ({round.ThreadId},{round.Iteration})");

                        if (round.First is null || round.First.Name != tag || round.First.Value != expectedValue)
                            mismatches.Add($"[{tag}] expected First=({tag},{expectedValue}), got ({round.First?.Name},{round.First?.Value})");

                        if (round.Second is null || round.Second.Name != tag || round.Second.Value != expectedValue)
                            mismatches.Add($"[{tag}] expected Second=({tag},{expectedValue}), got ({round.Second?.Name},{round.Second?.Value})");
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            exceptions.Should().BeEmpty(because: "concurrent FromBinary calls must not share mutable JsonSerializer state");
            mismatches.Should().BeEmpty(because: "each thread's object graph must round-trip independently of every other thread's");
        }
    }
}
