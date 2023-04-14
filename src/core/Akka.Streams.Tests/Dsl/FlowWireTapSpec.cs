//-----------------------------------------------------------------------
// <copyright file="FlowWireTapSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static FluentAssertions.FluentActions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowWireTapSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowWireTapSpec(ITestOutputHelper helper)
            : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public async Task A_wireTap_must_call_the_procedure_for_each_element()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                Source.From(Enumerable.Range(1, 100))                                                                             
                .WireTap(i => TestActor.Tell(i))                                                                             
                .RunWith(Sink.Ignore<int>(), Materializer).Wait();
                foreach (var i in Enumerable.Range(1, 100))
                    await ExpectMsgAsync(i);
            }, Materializer);
        }

        [Fact]
        public async Task A_wireTap_must_complete_the_future_for_an_empty_stream()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                await Source.Empty<string>()                                                                             
                .WireTap(i => TestActor.Tell(i))                                                                             
                .RunWith(Sink.Ignore<string>(), Materializer)                                                                             
                .ContinueWith(_ => TestActor.Tell("done"));
                await ExpectMsgAsync("done");
            }, Materializer);
        }

        [Fact]
        public async Task A_wireTap_must_yield_the_first_error()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var p = this.CreateManualPublisherProbe<int>();

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                Source.FromPublisher(p)
                    .WireTap(i => TestActor.Tell(i))
                    .RunWith(Sink.Ignore<int>(), Materializer)
                    .ContinueWith(t => TestActor.Tell(t.Exception.InnerException));
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

                var proc = await p.ExpectSubscriptionAsync();
                await proc.ExpectRequestAsync();
                var rte = new Exception("ex");
                proc.SendError(rte);
                await ExpectMsgAsync(rte);
            }, Materializer);
        }

        [Fact]
        public async Task A_wireTap_must_no_cause_subsequent_stages_to_be_failed_if_throws()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var error = new TestException("Boom!");
                var future = Source.Single(1).WireTap(_ => throw error).RunWith(Sink.Ignore<int>(), Materializer);
                Invoking(() => future.Wait()).Should().NotThrow();
                return Task.CompletedTask;
            }, Materializer);
        }
    }
}
