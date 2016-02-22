using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.Util;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowMapSpec : ScriptedTest
    {
        private readonly ActorMaterializerSettings _settings;
        private readonly ActorMaterializer _materializer;

        public FlowMapSpec()
        {
            Sys.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());
            _settings = ActorMaterializerSettings.Create(Sys)
                .WithInputBuffer(initialSize: 2, maxSize: 16);

            _materializer = Sys.Materializer(_settings);
        }

        [Fact]
        public void Map_should_map()
        {

            var script = Script.Create(Enumerable.Range(1, ThreadLocalRandom.Current.Next(1, 10)).Select(_ =>
            {
                var x = ThreadLocalRandom.Current.Next();
                return Tuple.Create(new[] {x} as IEnumerable<int>, new[] {x.ToString()} as IEnumerable<string>);
            }).ToArray());

            var n = ThreadLocalRandom.Current.Next(10);
            for (int i = 0; i < n; i++)
            {
                RunScript(script, _settings, x => x.Map(y => y.ToString()));
            }
        }

        [Fact]
        public void Map_should_not_blow_up_with_high_request_counts()
        {
            var probe = this.CreateManualProbe<int>();

            Source.From(new [] {1})
                .Map(x => x + 1)
                .Map(x => x + 1)
                .Map(x => x + 1)
                .Map(x => x + 1)    
                .RunWith(Sink.AsPublisher<int>(false), _materializer)
                .Subscribe(probe);

            var subscription = probe.ExpectSubscription();
            for (int i = 1; i <= 10000; i++)
            {
                subscription.Request(int.MaxValue);
            }

            probe.ExpectNext(6);
            probe.ExpectComplete();
        }
    }
}