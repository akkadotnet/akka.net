//-----------------------------------------------------------------------
// <copyright file="FlowTakeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Actors;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Akka.Streams.Tests.Dsl.TestConfig;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowTakeSpec : ScriptedTest
    {
        private ActorMaterializer Materializer { get; }

        public FlowTakeSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);

            MuteDeadLetters(typeof(OnNext), typeof(OnComplete), typeof(RequestMore));
        }

        [Fact]
        public void A_Take_must_take()
        {
            Func<int, Script<int, int>> script =
                d => Script.Create(RandomTestRange(Sys).Select(n => ((ICollection<int>)new[] { n }, (ICollection<int>)(n > d ? new int[]{ } : new[] { n }))).ToArray());
            var random = new Random();
            RandomTestRange(Sys).ForEach(_ =>
            {
                var d = Math.Min(Math.Max(random.Next(-10, 60), 0), 50);
                RunScript(script(d), Materializer.Settings, f => f.Take(d));
            });
        }

        [Fact]
        public void A_Take_must_not_Take_anything_for_negative_n()
        {
            var probe = this.CreateManualSubscriberProbe<int>();
            Source.From(Enumerable.Range(1, 3))
                .Take(-1)
                .To(Sink.FromSubscriber(probe))
                .Run(Materializer);
            probe.ExpectSubscription().Request(10);
            probe.ExpectComplete();
        }

        [Fact]
        public void A_Take_complete_eagerly_when_zero_or_less_is_taken_independently_of_upstream_completion()
        {
            Source.Maybe<int>()
                .Take(0)
                .RunWith(Sink.Ignore<int>(), Materializer)
                .Wait(TimeSpan.FromSeconds(3))
                .Should()
                .BeTrue();

            Source.Maybe<int>()
                .Take(-1)
                .RunWith(Sink.Ignore<int>(), Materializer)
                .Wait(TimeSpan.FromSeconds(3))
                .Should()
                .BeTrue();
        }
    }
}
