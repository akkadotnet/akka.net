//-----------------------------------------------------------------------
// <copyright file="FlowDetacherSpec.cs" company="Akka.NET Project">
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
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowDetacherSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowDetacherSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        [Fact]
        public void A_Detacher_must_pass_through_all_elements()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 100))
                    .Detach()
                    .RunWith(Sink.Seq<int>(), Materializer)
                    .Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 100));
            }, Materializer);
        }

        [Fact]
        public void A_Detacher_must_pass_through_failure()
        {
            this.AssertAllStagesStopped(() =>
            {
                var ex = new TestException("buh");
                var result = Source.From(Enumerable.Range(1, 100)).Select(x =>
                {
                    if (x == 50)
                        throw ex;
                    return x;
                }).Detach().RunWith(Sink.Seq<int>(), Materializer);

                result.Invoking(r => r.Wait(TimeSpan.FromSeconds(2))).ShouldThrow<TestException>().And.Should().Be(ex);
            }, Materializer);
        }

        [Fact]
        public void A_Detacher_must_emit_the_last_element_when_completed_Without_demand()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = Source.Single(42).Detach().RunWith(this.SinkProbe<int>(), Materializer).EnsureSubscription();
                probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
                probe.RequestNext(42);
            }, Materializer);
        }
    }
}
