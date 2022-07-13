//-----------------------------------------------------------------------
// <copyright file="ActorSubscriberSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Routing;
using Akka.Streams.Actors;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.Tests.Actor;
using Akka.TestKit;
using FluentAssertions;
using Reactive.Streams;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class AsyncEnumerableSourceSpec : AkkaSpec
    {
        private ITestOutputHelper _helper;
        public AsyncEnumerableSourceSpec(ITestOutputHelper helper)
            : base(
                AkkaSpecConfig.WithFallback(StreamTestDefaultMailbox.DefaultConfig),
                helper)
        {
            _helper = helper;
        }
        [Fact]
        public async Task AsyncEnumerableSource_must_allow_multiple_IAsyncEnumerable()
        {
            var materializer = Sys.Materializer();
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                // creating actor with default supervision, because stream supervisor default strategy is to 
                var actorRef = Sys.ActorOf(ManualSubscriber.Props(TestActor));
                Source.AsyncEnumerableAsRun(RangeAsync(1, 7))
                    .RunWith(Sink.FromSubscriber(new ActorSubscriberImpl<int>(actorRef)), materializer);
                actorRef.Tell("ready");
                (await ExpectMsgAsync<OnNext>()).Element.Should().Be(1);
                (await ExpectMsgAsync<OnNext>()).Element.Should().Be(2);
                await ExpectNoMsgAsync(200);
                actorRef.Tell("boom");
                actorRef.Tell("ready");
                actorRef.Tell("ready");
                actorRef.Tell("boom");
                await foreach (var n in RangeAsync(3, 4))
                {
                    (await ExpectMsgAsync<OnNext>()).Element.Should().Be(n);
                }
                await ExpectNoMsgAsync(200);
                actorRef.Tell("ready");
                (await ExpectMsgAsync<OnNext>()).Element.Should().Be(7);
                await ExpectMsgAsync<OnComplete>();
            }, materializer);
        }
        
        [Fact]
        public void AsyncEnumerableSource_Must_Complete_Immediately_With_No_elements_When_An_Empty_IAsyncEnumerable_Is_Passed_In()
        {
            var p = Source.AsyncEnumerableAsRun(AsyncEnumerable.Empty<int>()).RunWith(Sink.AsPublisher<int>(false), Sys.Materializer());
            var c = this.CreateManualSubscriberProbe<int>();
            p.Subscribe(c);
            c.ExpectSubscriptionAndComplete();
        }

        [Fact]
        public void AsyncEnumerableSource_Select()
        {
            var materializer = Sys.Materializer();
            var p = Source.AsyncEnumerableAsRun(RangeAsync(1, 100))
                    .Select(message =>
                    {
                        return message;
                    });
            ///////////
        }
        static async IAsyncEnumerable<int> RangeAsync(int start, int count)
        {
            for (int i = 0; i < count; i++)
            {
                await Task.Delay(i);
                yield return start + i;
            }
        }
    }
   
}
