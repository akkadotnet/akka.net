//-----------------------------------------------------------------------
// <copyright file="TwoStreamsSetup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Reactive.Streams;
using Xunit.Abstractions;

namespace Akka.Streams.TestKit.Tests
{
    public abstract class TwoStreamsSetup<TOutputs> : BaseTwoStreamsSetup<TOutputs>
    {
        protected TwoStreamsSetup(ITestOutputHelper helper) : base(helper)
        {
            
        }

        protected abstract class Fixture
        {
            protected GraphDsl.Builder<NotUsed> Builder { get; private set; }

            protected Fixture(GraphDsl.Builder<NotUsed> builder)
            {
                Builder = builder;
            }

            public abstract Inlet<int> Left { get; }
            public abstract Inlet<int> Right { get; }
            public abstract Outlet<TOutputs> Out { get; }
        }

        protected abstract Fixture CreateFixture(GraphDsl.Builder<NotUsed> builder);

        protected override TestSubscriber.Probe<TOutputs> Setup(IPublisher<int> p1, IPublisher<int> p2)
        {
            var subscriber = this.CreateSubscriberProbe<TOutputs>();
            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var f = CreateFixture(b);

                b.From(Source.FromPublisher(p1)).To(f.Left);
                b.From(Source.FromPublisher(p2)).To(f.Right);
                b.From(f.Out).To(Sink.FromSubscriber(subscriber));
                return ClosedShape.Instance;
            })).Run(Materializer);

            return subscriber;
        }
    }
}
