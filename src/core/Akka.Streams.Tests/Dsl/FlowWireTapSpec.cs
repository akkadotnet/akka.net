//-----------------------------------------------------------------------
// <copyright file="FlowWireTapSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
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
        public void A_wireTap_must_call_the_procedure_for_each_element()
        {
            this.AssertAllStagesStopped(() =>
            {   
                Source.From(Enumerable.Range(1, 100))
                    .WireTap(i => TestActor.Tell(i))
                    .RunWith(Sink.Ignore<int>(), Materializer).Wait();

                Enumerable.Range(1, 100).Select(i => ExpectMsg(i));
            }, Materializer);
        }

        [Fact]
        public void A_wireTap_must_complete_the_future_for_an_empty_stream()
        {
            this.AssertAllStagesStopped(() =>
            {   
                Source.Empty<string>()
                    .WireTap(i => TestActor.Tell(i))
                    .RunWith(Sink.Ignore<string>(), Materializer)
                    .ContinueWith(_ => TestActor.Tell("done"));

                ExpectMsg("done");

            }, Materializer);
        }

        [Fact]
        public void A_wireTap_must_yield_the_first_error()
        {
            this.AssertAllStagesStopped(() =>
            {   
                var p = this.CreateManualPublisherProbe<int>();

                Source.FromPublisher(p)
                    .WireTap(i => TestActor.Tell(i))
                    .RunWith(Sink.Ignore<int>(), Materializer)
                    .ContinueWith(t => TestActor.Tell(t.Exception.InnerException));

                var proc = p.ExpectSubscription();
                proc.ExpectRequest();
                var rte = new Exception("ex");
                proc.SendError(rte);
                ExpectMsg(rte);

            }, Materializer);
        }

        [Fact]
        public void A_wireTap_must_no_cause_subsequent_stages_to_be_failed_if_throws()
        {
            this.AssertAllStagesStopped(() =>
            {   
                var error = new TestException("Boom!");
                var future = Source.Single(1).WireTap(_ => throw error).RunWith(Sink.Ignore<int>(), Materializer);
                Invoking(() => future.Wait()).Should().NotThrow();
            }, Materializer);
        }
    }
}
