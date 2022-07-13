//-----------------------------------------------------------------------
// <copyright file="ActorSubscriberSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        private ActorMaterializer Materializer { get; }

        public AsyncEnumerableSourceSpec(ITestOutputHelper helper)
            : base(
                AkkaSpecConfig.WithFallback(StreamTestDefaultMailbox.DefaultConfig),
                helper)
        {

        }

        [Fact]
        public async Task ActorSubscriber_should_receive_requested_elements()
        {
            var materializer = Sys.Materializer();
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var actorRef = Source.From(RangeAsync(10, 3))
                    .RunWith(Sink.ActorSubscriber<int>(ManualSubscriber.Props(TestActor)), materializer);

                await ExpectNoMsgAsync(200);
                actorRef.Tell("ready"); //requesting 2
                (await ExpectMsgAsync<OnNext>()).Element.Should().Be(1);
                (await ExpectMsgAsync<OnNext>()).Element.Should().Be(2);
                await ExpectNoMsgAsync(200);
                actorRef.Tell("ready");
                (await ExpectMsgAsync<OnNext>()).Element.Should().Be(3);
                await ExpectMsgAsync<OnComplete>();
            }, materializer);
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
