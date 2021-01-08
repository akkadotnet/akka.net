//-----------------------------------------------------------------------
// <copyright file="InterpreterStressSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Diagnostics;
using System.Linq;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using OnNext = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup.OnNext;
using Cancel = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup.Cancel;
using OnComplete = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup.OnComplete;
using RequestOne = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup.RequestOne;

namespace Akka.Streams.Tests.Implementation.Fusing
{
    public class InterpreterStressSpec : GraphInterpreterSpecKit
    {
        //TODO performance!!! we need 1000 * 1000 here
        private const int ChainLength = 1000 * 100;
        private const int HalfLength = ChainLength / 2;
        private const int Repetition = 100;

        private readonly ITestOutputHelper _helper;

        // GraphStages can be reused
        private static readonly Select<int, int> Select = new Select<int, int>(x => x + 1);
        private static readonly Skip<int> SkipOne = new Skip<int>(1);
        private static readonly Take<int> TakeOne = new Take<int>(1);
        private static readonly Take<int> TakeHalfOfRepetition = new Take<int>(Repetition/2);

        public InterpreterStressSpec(ITestOutputHelper helper = null) : base(helper)
        {
            _helper = helper;
        }


        [Fact]
        public void Interpreter_must_work_with_a_massive_chain_of_maps()
        {
            var ops = Enumerable.Range(1, ChainLength).Select(_ => Select).ToArray();
            WithOneBoundedSetup(ops, (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();
                    var tstamp = new Stopwatch();
                    tstamp.Start();

                    var i = 0;
                    while (i < Repetition)
                    {
                        downstream.RequestOne();
                        lastEvents().Should().BeEquivalentTo(new RequestOne());

                        upstream.OnNext(i);
                        lastEvents().Should().BeEquivalentTo(new OnNext(i + ChainLength));
                        i++;
                    }

                    upstream.OnComplete();
                    lastEvents().Should().BeEquivalentTo(new OnComplete());

                    tstamp.Stop();
                    var time = tstamp.Elapsed.TotalSeconds;
                    // Not a real benchmark, just for sanity check
                    _helper?.WriteLine($"Chain finished in {time} seconds {ChainLength * Repetition} maps in total and {(ChainLength * Repetition) / (time * 1000 * 1000)} million maps/s");
                });
        }

        [Fact]
        public void Interpreter_must_work_with_a_massive_chain_of_maps_with_early_complete()
        {
            var ops =
                Enumerable.Range(1, HalfLength)
                    .Select(_ => Select)
                    .ToList<IGraphStageWithMaterializedValue<FlowShape<int,int>, object>>();
            ops.Add(TakeHalfOfRepetition);
            ops.AddRange(Enumerable.Range(1, HalfLength).Select(_ => Select));

            WithOneBoundedSetup(ops.ToArray(), (lastEvents, upstream, downstream) =>
            {
                lastEvents().Should().BeEmpty();
                var tstamp = new Stopwatch();
                tstamp.Start();

                var i = 0;
                while (i < (Repetition/2) - 1)
                {
                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(i);
                    lastEvents().Should().BeEquivalentTo(new OnNext(i + ChainLength));
                    i++;
                }

                downstream.RequestOne();
                lastEvents().Should().BeEquivalentTo(new RequestOne());

                upstream.OnNext(0);
                lastEvents().Should().BeEquivalentTo(new OnNext(0 + ChainLength), new Cancel(), new OnComplete());

                tstamp.Stop();
                var time = tstamp.Elapsed.TotalSeconds;
                // Not a real benchmark, just for sanity check
                _helper?.WriteLine(
                    $"Chain finished in {time} seconds {ChainLength*Repetition} maps in total and {(ChainLength*Repetition)/(time*1000*1000)} million maps/s");
            });
        }

        [Fact]
        public void Interpreter_must_work_with_a_massive_chain_of_takes()
        {
            var ops = Enumerable.Range(1, ChainLength / 10).Select(_ => TakeOne).ToArray();
            WithOneBoundedSetup(ops, (lastEvents, upstream, downstream) =>
            {
                lastEvents().Should().BeEmpty();

                downstream.RequestOne();
                lastEvents().Should().BeEquivalentTo(new RequestOne());

                upstream.OnNext(0);
                lastEvents().Should().BeEquivalentTo(new OnNext(0), new Cancel(), new OnComplete());
            });
        }

        [Fact]
        public void Interpreter_must_work_with_a_massive_chain_of_drops()
        {
            var ops = Enumerable.Range(1, ChainLength/1000).Select(_ => SkipOne).ToArray();

            WithOneBoundedSetup(ops, (lastEvents, upstream, downstream) =>
            {
                lastEvents().Should().BeEmpty();

                downstream.RequestOne();
                lastEvents().Should().BeEquivalentTo(new RequestOne());

                var i = 0;
                while (i < (ChainLength / 1000))
                {
                    upstream.OnNext(0);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());
                    i++;
                }

                upstream.OnNext(0);
                lastEvents().Should().BeEquivalentTo(new OnNext(0));
            });

        }

        [Fact]
        public void Interpreter_must_work_with_a_massive_chain_of_batches_of_overflowing_to_the_heap()
        {
            var batch = new Batch<int, int>(0, _ => 0, i => i, (agg, i) => agg + i);
            var ops = Enumerable.Range(1, ChainLength/10).Select(_ => batch).ToArray();

            WithOneBoundedSetup(ops, (lastEvents, upstream, downstream) =>
            {
                lastEvents().Should().BeEquivalentTo(new RequestOne());

                var i = 0;
                while (i < Repetition)
                {
                    upstream.OnNext(1);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());
                    i++;
                }
            });
        }
    }
}
