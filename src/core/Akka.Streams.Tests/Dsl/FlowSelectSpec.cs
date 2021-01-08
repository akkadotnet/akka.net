//-----------------------------------------------------------------------
// <copyright file="FlowSelectSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.Util;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowSelectSpec : ScriptedTest
    {
        private readonly ActorMaterializerSettings _settings;
        private readonly ActorMaterializer _materializer;

        public FlowSelectSpec(ITestOutputHelper output) : base(output)
        {
            Sys.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());
            _settings = ActorMaterializerSettings.Create(Sys)
                .WithInputBuffer(initialSize: 2, maxSize: 16);

            _materializer = Sys.Materializer(_settings);
        }

        [Fact]
        public void Select_should_select()
        {

            var script = Script.Create(Enumerable.Range(1, ThreadLocalRandom.Current.Next(1, 10)).Select(_ =>
            {
                var x = ThreadLocalRandom.Current.Next();
                return ((ICollection<int>)new[] {x}, (ICollection<string>)new[] {x.ToString()});
            }).ToArray());

            var n = ThreadLocalRandom.Current.Next(10);
            for (int i = 0; i < n; i++)
            {
                RunScript(script, _settings, x => x.Select(y => y.ToString()));
            }
        }

        [Fact]
        public void Select_should_not_blow_up_with_high_request_counts()
        {
            var probe = this.CreateManualSubscriberProbe<int>();

            Source.From(new [] {1})
                .Select(x => x + 1)
                .Select(x => x + 1)
                .Select(x => x + 1)
                .Select(x => x + 1)
                .Select(x => x + 1)
                .RunWith(Sink.AsPublisher<int>(false), _materializer)
                .Subscribe(probe);

            var subscription = probe.ExpectSubscription();

            for (int i = 1; i <= 10000; i++)
                subscription.Request(int.MaxValue);

            probe.ExpectNext(6);
            probe.ExpectComplete();
        }
    }
}
