//-----------------------------------------------------------------------
// <copyright file="SubscriberSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class SubscriberSourceSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public SubscriberSourceSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void A_SubscriberSource_must_be_able_to_use_Subscribe_in_materialized_value_transformation()
        {
            var f = Source.AsSubscriber<int>()
                .MapMaterializedValue(
                    s => Source.From(Enumerable.Range(1, 3)).RunWith(Sink.FromSubscriber(s), Materializer))
                .RunWith(Sink.Aggregate<int, int>(0, (sum, i) => sum + i), Materializer);

            f.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            f.Result.Should().Be(6);
        }
    }
}
