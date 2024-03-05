//-----------------------------------------------------------------------
// <copyright file="FlowWhereSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Xunit;
using Xunit.Abstractions;
using static Akka.Streams.Tests.Dsl.TestConfig;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    // JVM: FlowFilterSpec
    public class FlowWhereSpec : ScriptedTest
    {
        private ActorMaterializer Materializer { get; }
        private ActorMaterializerSettings Settings { get; }

        public FlowWhereSpec(ITestOutputHelper helper) : base(helper)
        {
            Settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, Settings);
        }

        [Fact]
        public void A_Where_must_filter()
        {
            var random = new Random();
            Script<int, int> script = Script.Create(RandomTestRange(Sys).Select(_ =>
            {
                var x = random.Next();
                return ((ICollection<int>)new[] { x }, (ICollection<int>)((x & 1) == 0 ? new[] { x } : new int[] { }));
            }).ToArray());

            RandomTestRange(Sys).Select(async _ => await RunScriptAsync(script, Settings, flow => flow.Where(x => x%2 == 0)));
        }

        [Fact]
        public async Task A_Where_must_not_blow_up_with_high_request_counts()
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(1, 1);
            var materializer = ActorMaterializer.Create(Sys, settings);

            var probe = this.CreateManualSubscriberProbe<int>();
            Source.From(Enumerable.Repeat(0, 1000).Concat(new[] {1}))
                .Where(x => x != 0)
                .RunWith(Sink.FromSubscriber(probe), materializer);

            var subscription = probe.ExpectSubscription();
            for (var i = 1; i <= 1000; i++)
                subscription.Request(int.MaxValue);

            await probe.ExpectNextAsync(1);
            await probe.ExpectCompleteAsync();
        }

        [Fact]
        public async Task A_Where_must_continue_if_error()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var ex = new TestException("Test");

                await Source.From(Enumerable.Range(1, 3))
                    .Where(x =>
                    {
                        if (x == 2)
                            throw ex;
                        return true;
                    })
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(3)
                    .ExpectNext(1, 3)
                    .ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public void A_WhereNot_must_filter_based_on_inverted_predicate()
        {
            var random = new Random();
            Script<int, int> script = Script.Create(RandomTestRange(Sys).Select(_ =>
            {
                var x = random.Next();
                return ((ICollection<int>)new[] { x }, (ICollection<int>)((x & 1) == 1 ? new[] { x } : new int[] { }));
            }).ToArray());

            RandomTestRange(Sys).Select(async _ => await RunScriptAsync(script, Settings, flow => flow.WhereNot(x => x % 2 == 0)));
        }
    }
}
