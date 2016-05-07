//-----------------------------------------------------------------------
// <copyright file="FlowSkipWhileSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowSkipWhileSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowSkipWhileSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void A_SkipWhile_must_drop_while_predicate_is_true()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 4))
                    .SkipWhile(x => x < 3)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(2)
                    .ExpectNext(3, 4)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_SkipWhile_must_complete_the_future_for_an_empty_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.Empty<int>()
                    .SkipWhile(x => x < 2)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(1)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_SkipWhile_must_continue_if_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                var testException = new Exception("test");
                Source.From(Enumerable.Range(1, 4)).SkipWhile(x =>
                {
                    if (x < 3)
                        return true;
                    throw testException;
                })
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(1)
                    .ExpectComplete();
            }, Materializer);
        }
    }
}
