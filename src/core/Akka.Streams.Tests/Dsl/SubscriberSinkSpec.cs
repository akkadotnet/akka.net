//-----------------------------------------------------------------------
// <copyright file="SubscriberSinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using System.Reactive.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class SubscriberSinkSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public SubscriberSinkSpec(ITestOutputHelper helper = null) : base(null)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys,settings);
        }

        [Fact]
        public void A_Flow_with_SubscriberSink_must_publish_elements_to_the_subscriber()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c = this.CreateManualProbe<int>();
                Source.From(Enumerable.Range(1, 3)).To(Sink.FromSubscriber(c)).Run(Materializer);

                var s = c.ExpectSubscription();
                s.Request(3);
                c.ExpectNext(1);
                c.ExpectNext(2);
                c.ExpectNext(3);
                c.ExpectComplete();
            }, Materializer);
        }
    }
}
