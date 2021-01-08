//-----------------------------------------------------------------------
// <copyright file="FlowAppendSpec.cs" company="Akka.NET Project">
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
using Akka.TestKit;
using FluentAssertions;
using Reactive.Streams;
using Xunit;
using Xunit.Abstractions;
using static Akka.Streams.Tests.Dsl.FlowAppendSpec.River;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowAppendSpec : Akka.TestKit.AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowAppendSpec(ITestOutputHelper helper) : base (helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void Flow_should_append_Flow()
        {
            RiverOf<string>((subscriber, otherFlow, elements) =>
            {
                var flow = Flow.Create<int>().Via(otherFlow);
                Source.From(elements).Via(flow).To(Sink.FromSubscriber(subscriber)).Run(Materializer);
            }, this);
        }

        [Fact]
        public void Flow_should_append_Sink()
        {
            RiverOf<string>((subscriber, otherFlow, elements) =>
            {
                Source.From(elements).To(otherFlow.To(Sink.FromSubscriber(subscriber))).Run(Materializer);
            }, this);
        }

        [Fact]
        public void Source_should_append_Flow()
        {
            RiverOf<string>((subscriber, otherFlow, elements) =>
            {
                Source.From(elements).Via(otherFlow).To(Sink.FromSubscriber(subscriber)).Run(Materializer);
            }, this);
        }

        [Fact]
        public void Source_should_append_Sink()
        {
            RiverOf<string>((subscriber, otherFlow, elements) =>
            {
                Source.From(elements).To(otherFlow.To(Sink.FromSubscriber(subscriber))).Run(Materializer);
            }, this);
        }

        internal static class River
        {
            private static readonly Flow<int, string, NotUsed> OtherFlow = Flow.Create<int>().Select(i => i.ToString());
            
            public static void RiverOf<T>(Action<ISubscriber<T>, Flow<int, string, NotUsed>, IEnumerable<int>> flowConstructor, TestKitBase kit)
            {
                var subscriber = kit.CreateManualSubscriberProbe<T>();

                var elements = Enumerable.Range(1, 10).ToList();
                flowConstructor(subscriber, OtherFlow, elements);

                var subscription = subscriber.ExpectSubscription();
                subscription.Request(elements.Count);
                elements.ForEach(el=>subscriber.ExpectNext().Should().Be(el.ToString()));
                subscription.Request(1);
                subscriber.ExpectComplete();
            }
        }
    }
}
