//-----------------------------------------------------------------------
// <copyright file="FlowCollectSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

namespace Akka.Streams.Tests.Dsl
{
    public class FlowCollectSpec : ScriptedTest
    {
        private Random Random { get; } = new Random(12345);
        private ActorMaterializer Materializer { get; }

        public FlowCollectSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        // No need to use AssertAllStagesStoppedAsync, it is encapsulated in RunScriptAsync
        [Fact]
        public async Task An_old_behaviour_Collect_must_collect()
        {
            var script = Script.Create(RandomTestRange(Sys).Select(_ =>
            {
                var x = Random.Next(0, 10000);
                return ((ICollection<int>)new[] { x },
                    (x & 1) == 0 ? (ICollection<string>)new[] { (x * x).ToString() } : new string[] { });
            }).ToArray());
            
            foreach (var _ in RandomTestRange(Sys))
            {
                await RunScriptAsync(script, Materializer.Settings,
                    // This is intentional, testing backward compatibility with old obsolete method
#pragma warning disable CS0618
                    flow => flow.Collect(x => x % 2 == 0 ? (x * x).ToString() : null), 
#pragma warning restore CS0618
                    spec: this);
            }
        }

        // No need to use AssertAllStagesStoppedAsync, it is encapsulated in RunScriptAsync
        [Fact]
        public async Task A_Collect_must_collect()
        {
            var script = Script.Create(RandomTestRange(Sys).Select(_ =>
            {
                var x = Random.Next(0, 10000);
                return ((ICollection<int>)new[] { x },
                    (x & 1) == 0 ? (ICollection<string>)new[] { (x*x).ToString() } : new string[] {});
            }).ToArray());

            foreach (var _ in RandomTestRange(Sys))
            {
                await RunScriptAsync(script, Materializer.Settings,
                    flow => flow.Collect(x => x % 2 == 0, x => (x * x).ToString()), spec: this);
            }
        }

        [Fact]
        public async Task An_old_behaviour_Collect_must_restart_when_Collect_throws()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                int ThrowOnTwo(int x) => x == 2 ? throw new TestException("") : x;

                var probe =
                    Source.From(Enumerable.Range(1, 3))
                        // This is intentional, testing backward compatibility with old obsolete method 
#pragma warning disable CS0618
                        .Collect(ThrowOnTwo)
#pragma warning restore CS0618
                        .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                        .RunWith(this.SinkProbe<int>(), Materializer);

                await probe.AsyncBuilder()
                    .Request(1)
                    .ExpectNext(1)
                    .Request(1)
                    .ExpectNext(3)
                    .Request(1)
                    .ExpectComplete()
                    .ExecuteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Collect_must_restart_when_Collect_throws()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                bool ThrowOnTwo(int x) => x == 2 ? throw new TestException("") : true;

                var probe =
                    Source.From(Enumerable.Range(1, 3))
                        .Collect(ThrowOnTwo, x => x)
                        .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                        .RunWith(this.SinkProbe<int>(), Materializer);

                await probe.AsyncBuilder()
                    .Request(1)
                    .ExpectNext(1)
                    .Request(1)
                    .ExpectNext(3)
                    .Request(1)
                    .ExpectComplete()
                    .ExecuteAsync();
            }, Materializer);
        }
    }
}
