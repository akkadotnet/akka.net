//-----------------------------------------------------------------------
// <copyright file="FlowRecoverSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.Streams.Util;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowRecoverSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowRecoverSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(1, 1);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private static readonly TestException Ex = new TestException("test");

        [Fact]
        public void A_Recover_must_recover_when_there_is_a_handler()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 4)).Select(x =>
                {
                    if (x == 3)
                        throw Ex;
                    return x;
                })
                    .Recover(_ => new Option<int>(0))
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .RequestNext(1)
                    .RequestNext(2)
                    .RequestNext(0)
                    .Request(1)
                    .ExpectComplete();

            }, Materializer);
        }

        [Fact]
        public void A_Recover_must_failed_stream_if_handler_is_not_for_such_exception_type()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 3)).Select(x =>
                {
                    if (x == 2)
                        throw Ex;
                    return x;
                })
                    .Recover(_ => Option<int>.None)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .RequestNext(1)
                    .Request(1)
                    .ExpectError().Should().Be(Ex);
            }, Materializer);
        }

        [Fact]
        public void A_Recover_must_not_influence_stream_when_there_is_no_exception()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 3))
                    .Select(x => x)
                    .Recover(_ => new Option<int>(0))
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(3)
                    .ExpectNext(1, 2, 3)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Recover_must_finish_stream_if_it_is_empty()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.Empty<int>()
                    .Select(x => x)
                    .Recover(_ => new Option<int>(0))
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(1)
                    .ExpectComplete();
            }, Materializer);
        }
    }
}
