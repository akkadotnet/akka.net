//-----------------------------------------------------------------------
// <copyright file="FlowTakeWithinSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowTakeWithinSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowTakeWithinSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        [Fact]
        public void A_TakeWithin_must_deliver_elements_within_the_duration_but_not_afterwards()
        {
            var input = 1;
            var p = this.CreateManualPublisherProbe<int>();
            var c = this.CreateManualSubscriberProbe<int>();
            Source.FromPublisher(p)
                .TakeWithin(TimeSpan.FromSeconds(1))
                .To(Sink.FromSubscriber(c))
                .Run(Materializer);
            var pSub = p.ExpectSubscription();
            var cSub = c.ExpectSubscription();
            cSub.Request(100);
            var demand1 = (int)pSub.ExpectRequest();
            Enumerable.Range(1,demand1).ForEach(_=>pSub.SendNext(input++));
            var demand2 = (int) pSub.ExpectRequest();
            Enumerable.Range(1, demand2).ForEach(_ => pSub.SendNext(input++));
            var demand3 = (int)pSub.ExpectRequest();
            var sentN = demand1 + demand2;
            Enumerable.Range(1, sentN).ForEach(n => c.ExpectNext(n));
            Within(TimeSpan.FromSeconds(2), c.ExpectComplete);
            Enumerable.Range(1, demand3).ForEach(_ => pSub.SendNext(input++));
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
        }

        [Fact]
        public void A_TakeWithin_must_deliver_buffered_elements_OnComplete_before_the_timeout()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c = this.CreateManualSubscriberProbe<int>();
                Source.From(Enumerable.Range(1, 3))
                    .TakeWithin(TimeSpan.FromSeconds(1))
                    .To(Sink.FromSubscriber(c))
                    .Run(Materializer);
                var cSub = c.ExpectSubscription();
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
                cSub.Request(100);
                Enumerable.Range(1, 3).ForEach(n => c.ExpectNext(n));
                c.ExpectComplete();
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
            }, Materializer);
        }
    }
}
