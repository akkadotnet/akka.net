//-----------------------------------------------------------------------
// <copyright file="FlowAlsoToSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using System.Threading.Tasks;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl;

public class FlowAlsoToSpec: AkkaSpec
{
    private ActorMaterializer Materializer { get; }

    public FlowAlsoToSpec(ITestOutputHelper output) : base(output)
    {
        Materializer = ActorMaterializer.Create(Sys);
    }

    [Fact(DisplayName = "AlsoTo with no failure propagation should not cancel parent stream")]
    public async Task AlsoToNoFailurePropagationTest()
    {
        await this.AssertAllStagesStoppedAsync(async () => {
            var downstream = this.CreateSubscriberProbe<int>();
            var alsoDownstream = this.CreateSubscriberProbe<int>();

            var tapFlow = Flow.Create<int>()
                .Select(i => i == 3 ? throw new TestException("equals 3") : i)
                .To(Sink.FromSubscriber(alsoDownstream));
            
            Source.From(Enumerable.Range(1, 5))
                .AlsoTo(tapFlow)
                .RunWith(Sink.FromSubscriber(downstream), Materializer);

            await downstream.ExpectSubscriptionAsync();
            await alsoDownstream.ExpectSubscriptionAsync();
            await downstream.RequestAsync(3);
            await alsoDownstream.RequestAsync(3);
            
            await downstream.ExpectNextAsync(1);
            await alsoDownstream.ExpectNextAsync(1);
            await downstream.ExpectNextAsync(2);
            await alsoDownstream.ExpectNextAsync(2);
            await downstream.ExpectNextAsync(3);
            
            // AlsoTo flow errored
            var ex = await alsoDownstream.ExpectErrorAsync();
            ex.Should().BeOfType<TestException>();
            ex.Message.Should().Be("equals 3");
            
            // Parent stream should still work till complete
            await downstream.RequestAsync(3);
            await downstream.ExpectNextAsync(4);
            await downstream.ExpectNextAsync(5);
            await downstream.ExpectCompleteAsync();
        }, Materializer);
    }

    [Fact(DisplayName = "AlsoTo with failure propagation should cancel parent stream")]
    public async Task AlsoToFailurePropagationTest()
    {
        await this.AssertAllStagesStoppedAsync(async () => {
            var downstream = this.CreateSubscriberProbe<int>();
            var alsoDownstream = this.CreateSubscriberProbe<int>();

            var tapFlow = Flow.Create<int>()
                .Select(i => i == 3 ? throw new TestException("equals 3") : i)
                .To(Sink.FromSubscriber(alsoDownstream));
            
            Source.From(Enumerable.Range(1, 5))
                .AlsoTo(tapFlow, true)
                .RunWith(Sink.FromSubscriber(downstream), Materializer);

            await downstream.ExpectSubscriptionAsync();
            await alsoDownstream.ExpectSubscriptionAsync();
            await downstream.RequestAsync(4);
            await alsoDownstream.RequestAsync(4);
            
            await downstream.ExpectNextAsync(1);
            await alsoDownstream.ExpectNextAsync(1);
            await downstream.ExpectNextAsync(2);
            await alsoDownstream.ExpectNextAsync(2);
            
            // AlsoTo flow errored
            var ex = await alsoDownstream.ExpectErrorAsync();
            ex.Should().BeOfType<TestException>();
            ex.Message.Should().Be("equals 3");
            
            // Parent stream should receive the last element and also error out
            await downstream.ExpectNextAsync(3);
            var ex2 = await downstream.ExpectErrorAsync();
            ex2.Should().Be(ex);
        }, Materializer);
    }
}
