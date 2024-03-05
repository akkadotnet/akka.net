//-----------------------------------------------------------------------
// <copyright file="FlowTakeWithinSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
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
        public async Task A_TakeWithin_must_deliver_elements_within_the_duration_but_not_afterwards()
        {
            var input = 1;
            var p = this.CreateManualPublisherProbe<int>();
            var c = this.CreateManualSubscriberProbe<int>();
            Source.FromPublisher(p)
                .TakeWithin(TimeSpan.FromSeconds(1))
                .To(Sink.FromSubscriber(c))
                .Run(Materializer);
            var pSub = await p.ExpectSubscriptionAsync();
            var cSub = await c.ExpectSubscriptionAsync();
            cSub.Request(100);

            var demand1 = (int)await pSub.ExpectRequestAsync();
            foreach (var n in Enumerable.Range(1, demand1))
                pSub.SendNext(input++);

            var demand2 = (int) await pSub.ExpectRequestAsync();
            foreach (var n in Enumerable.Range(1, demand2))
                pSub.SendNext(input++);

            var demand3 = (int)await pSub.ExpectRequestAsync();            
            var sentN = demand1 + demand2;
            foreach (var n in Enumerable.Range(1, sentN))
                await c.ExpectNextAsync(n);

            await WithinAsync(TimeSpan.FromSeconds(2), async() => await c.ExpectCompleteAsync());

            foreach (var n in Enumerable.Range(1, demand3))
                pSub.SendNext(input++);

            await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
        }

        [Fact]
        public async Task A_TakeWithin_must_deliver_buffered_elements_OnComplete_before_the_timeout()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var c = this.CreateManualSubscriberProbe<int>();
                Source.From(Enumerable.Range(1, 3))
                    .TakeWithin(TimeSpan.FromSeconds(1))
                    .To(Sink.FromSubscriber(c))
                    .Run(Materializer);
                var cSub = await c.ExpectSubscriptionAsync();
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
                cSub.Request(100);
                foreach (var n in Enumerable.Range(1, 3))
                    await c.ExpectNextAsync(n);
                await c.ExpectCompleteAsync();
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
            }, Materializer);
        }
    }
}
