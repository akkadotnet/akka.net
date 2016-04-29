//-----------------------------------------------------------------------
// <copyright file="FlowDropWhithinSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Reactive.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Dsl.Internal;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowDropWithinSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowDropWithinSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        [Fact]
        public void A_DropWithin_must_deliver_elements_after_the_duration_but_not_before()
        {
            var input = Enumerable.Range(1, 200).GetEnumerator();
            var p = TestPublisher.CreateManualProbe<int>(this);
            var c = TestSubscriber.CreateManualProbe<int>(this);
            Source.FromPublisher(p)
                .DropWithin(TimeSpan.FromSeconds(1))
                .To(Sink.FromSubscriber(c))
                .Run(Materializer);
            var pSub = p.ExpectSubscription();
            var cSub = c.ExpectSubscription();
            cSub.Request(100);
            var demand1 = pSub.ExpectRequest();
            Enumerable.Range(1, (int)demand1).ForEach(_ =>
            {
                input.MoveNext();
                pSub.SendNext(input.Current);
            });
            var demand2 = pSub.ExpectRequest();
            Enumerable.Range(1, (int)demand2).ForEach(_ =>
            {
                input.MoveNext();
                pSub.SendNext(input.Current);
            });
            var demand3 = pSub.ExpectRequest();
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(1500));
            Enumerable.Range(1, (int)demand3).ForEach(_ =>
            {
                input.MoveNext();
                pSub.SendNext(input.Current);
            });
            Enumerable.Range((int) (demand1 + demand2 + 1), (int)demand3)
                .ForEach(n => c.ExpectNext(n));
            pSub.SendComplete();
            c.ExpectComplete();
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
        }

        [Fact]
        public void A_DropWithin_must_deliver_completion_even_before_the_duration()
        {
            var upstream = TestPublisher.CreateProbe<int>(this);
            var downstream = TestSubscriber.CreateProbe<int>(this);

            Source.FromPublisher(upstream)
                .DropWithin(TimeSpan.FromDays(1))
                .RunWith(Sink.FromSubscriber(downstream), Materializer);

            upstream.SendComplete();
            downstream.ExpectSubscriptionAndComplete();
        }
    }
}
