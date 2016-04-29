//-----------------------------------------------------------------------
// <copyright file="FlowIntersperseSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowIntersperseSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowIntersperseSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void A_Intersperse_must_inject_element_between_existing_elements()
        {
            var probe =
                Source.From(new[] { 1, 2, 3 })
                    .Map(x => x.ToString())
                    .Intersperse(",")
                    .RunWith(this.SinkProbe<string>(), Materializer);

            probe.ExpectSubscription();
            probe.ToStrict(TimeSpan.FromSeconds(1)).Aggregate((s, s1) => s + s1).Should().Be("1,2,3");
        }

        [Fact]
        public void A_Intersperse_must_inject_element_between_existing_elements_when_downstream_is_fold()
        {
            var concated =
                Source.From(new[] { 1, 2, 3 })
                    .Map(x => x.ToString())
                    .Intersperse(",")
                    .RunFold("", (s, s1) => s + s1, Materializer);

            concated.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            concated.Result.Should().Be("1,2,3");
        }

        [Fact]
        public void A_Intersperse_must_inject_element_between_existing_elements_and_surround_with_start_and_end()
        {
            var probe =
                Source.From(new[] { 1, 2, 3 })
                    .Map(x => x.ToString())
                    .Intersperse("[", ",", "]")
                    .RunWith(this.SinkProbe<string>(), Materializer);

            probe.ExpectSubscription();
            probe.ToStrict(TimeSpan.FromSeconds(1)).Aggregate((s, s1) => s + s1).Should().Be("[1,2,3]");
        }

        [Fact]
        public void A_Intersperse_must_demonstrate_how_to_prepend_only()
        {
            var probe =
                Source.Combine(Source.Single(">> "), Source.From(new[] {"1", "2", "3"}).Intersperse(","),
                    i => new Merge<string, string>(i)).RunWith(this.SinkProbe<string>(), Materializer);

            probe.ExpectSubscription();
            probe.ToStrict(TimeSpan.FromSeconds(1)).Aggregate((s, s1) => s + s1).Should().Be(">> 1,2,3");
        }

        [Fact]
        public void A_Intersperse_must_surround_empty_stream_with_start_and_end()
        {
            var probe =
       Source.Empty<string>()
           .Map(x => x.ToString())
           .Intersperse("[", ",", "]")
           .RunWith(this.SinkProbe<string>(), Materializer);

            probe.ExpectSubscription();
            probe.ToStrict(TimeSpan.FromSeconds(1)).Aggregate((s, s1) => s + s1).Should().Be("[]");
        }

        [Fact]
        public void A_Intersperse_must_surround_single_element_stream_with_start_and_end()
        {
            var probe =
                Source.From(new[] {1})
                    .Map(x => x.ToString())
                    .Intersperse("[", ",", "]")
                    .RunWith(this.SinkProbe<string>(), Materializer);

            probe.ExpectSubscription();
            probe.ToStrict(TimeSpan.FromSeconds(1)).Aggregate((s, s1) => s + s1).Should().Be("[1]");
        }

        [Fact]
        public void A_Intersperse_must__complete_the_stage_when_the_Source_has_been_completed()
        {
            var t = this.SourceProbe<string>()
                .Intersperse(",")
                .ToMaterialized(this.SinkProbe<string>(), Keep.Both)
                .Run(Materializer);
            var p1 = t.Item1;
            var p2 = t.Item2;

            p2.Request(10);
            p1.SendNext("a")
                .SendNext("b")
                .SendComplete();
            p2.ExpectNext("a")
                .ExpectNext(",")
                .ExpectNext("b")
                .ExpectComplete();
        }

        [Fact]
        public void A_Intersperse_must_complete_the_stage_when_the_Sink_has_been_cancelled()
        {
            var t = this.SourceProbe<string>()
                .Intersperse(",")
                .ToMaterialized(this.SinkProbe<string>(), Keep.Both)
                .Run(Materializer);
            var p1 = t.Item1;
            var p2 = t.Item2;

            p2.Request(10);
            p1.SendNext("a")
                .SendNext("b");
            p2.ExpectNext("a")
                .ExpectNext(",");
            p2.Cancel();
            p1.ExpectCancellation();
        }
    }
}
