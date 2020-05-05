//-----------------------------------------------------------------------
// <copyright file="FlowCollectSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;
using static Akka.Streams.Tests.Dsl.TestConfig;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowCollectSpec : ScriptedTest
    {
        private ActorMaterializer Materializer { get; }

        public FlowCollectSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void A_Collect_must_collect()
        {
            var random = new Random();
            Script<int,string> script = Script.Create(RandomTestRange(Sys).Select(_ =>
            {
                var x = random.Next(0, 10000);
                return ((ICollection<int>)new[] { x },
                        (x & 1) == 0 ? (ICollection<string>)new[] { (x*x).ToString() } : (ICollection<string>)new string[] {});
            }).ToArray());

            RandomTestRange(Sys).ForEach(_=>RunScript(script, Materializer.Settings,flow => flow.Collect(x => x%2 == 0 ? (x*x).ToString() : null)));
        }

        [Fact]
        public void A_Collect_must_restart_when_Collect_throws()
        {
            Func<int, int> throwOnTwo = x =>
            {
                if (x == 2)
                    throw new TestException("");
                return x;
            };

            var probe =
                Source.From(Enumerable.Range(1, 3))
                    .Collect(throwOnTwo)
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                    .RunWith(this.SinkProbe<int>(), Materializer);
            probe.Request(1);
            probe.ExpectNext(1);
            probe.Request(1);
            probe.ExpectNext(3);
            probe.Request(1);
            probe.ExpectComplete();
        }
    }
}
