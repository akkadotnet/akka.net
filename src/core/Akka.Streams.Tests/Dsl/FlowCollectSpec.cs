using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Streams;
using System.Text;
using System.Threading.Tasks;
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
            Func<Script<int,string>> script = () => Script.Create(RandomTestRange(Sys).Select(_ =>
            {
                var x = random.Next(0, 10000);
                return new Tuple<IEnumerable<int>, IEnumerable<string>>(new[] {x},
                    (x & 1) == 0 ? new[] {(x*x).ToString()} : new string[] {});
            }).ToArray());

            RandomTestRange(Sys).ForEach(_=>RunScript(script(), Materializer.Settings,flow => flow.Collect(x => x%2 == 0 ? (x*x).ToString() : null)));
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
                    .WithAttributes(Attributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
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
