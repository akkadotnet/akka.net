//-----------------------------------------------------------------------
// <copyright file="FlowSkipSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Xunit;
using Xunit.Abstractions;
using static Akka.Streams.Tests.Dsl.TestConfig;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowSkipSpec : ScriptedTest
    {
        private ActorMaterializerSettings Settings { get; }
        private ActorMaterializer Materializer { get; }

        public FlowSkipSpec(ITestOutputHelper helper) : base(helper)
        {
            Settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, Settings);
        }

        [Fact]
        public void A_Skip_must_skip()
        {
            Func<long, Script<int, int>> script =
                d => Script.Create(RandomTestRange(Sys)
                            .Select(n => ((ICollection<int>)new[] { n }, (ICollection<int>)(n <= d ? new int[] { } : new[] { n })))
                            .ToArray());
            var random = new Random();
            foreach (var _ in RandomTestRange(Sys))
            {
                var d = Math.Min(Math.Max(random.Next(-10, 60), 0), 50);
                RunScript(script(d), Settings, f => f.Skip(d));
            }
        }

        [Fact]
        public void A_Skip_must_not_skip_anything_for_negative_n()
        {
            var probe = this.CreateManualSubscriberProbe<int>();
            Source.From(new[] {1, 2, 3}).Skip(-1).To(Sink.FromSubscriber(probe)).Run(Materializer);
            probe.ExpectSubscription().Request(10);
            probe.ExpectNext(1);
            probe.ExpectNext(2);
            probe.ExpectNext(3);
            probe.ExpectComplete();
        }
    }
}
