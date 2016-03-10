//-----------------------------------------------------------------------
// <copyright file="TwoStreamsSetup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Reactive.Streams;
using Akka.Streams.Dsl;

namespace Akka.Streams.TestKit.Tests
{
    public abstract class TwoStreamsSetup<TOutputs> : BaseTwoStreamsSetup<TOutputs>
    {
        protected abstract class Fixture
        {
            protected GraphDsl.Builder<Unit> Builder { get; private set; }

            protected Fixture(GraphDsl.Builder<Unit> builder)
            {
                Builder = builder;
            }

            public abstract Inlet<int> Left { get; }
            public abstract Inlet<int> Right { get; }
            public abstract Outlet<TOutputs> Out { get; }
        }

        protected abstract Fixture CreateFixture(GraphDsl.Builder<Unit> builder);

        protected override TestSubscriber.Probe<TOutputs> Setup(IPublisher<int> p1, IPublisher<int> p2)
        {
            var subscriber = TestSubscriber.CreateProbe<TOutputs>(this);
            RunnableGraph<Unit>.FromGraph(GraphDsl.Create<ClosedShape, Unit>(b =>
            {
                var f = CreateFixture(b);

                b.From(Source.FromPublisher<int, Unit>(p1)).To(f.Left);
                b.From(Source.FromPublisher<int, Unit>(p2)).To(f.Right);
                b.From(f.Out).To(Sink.FromSubscriber<TOutputs, Unit>(subscriber));
                return ClosedShape.Instance;
            })).Run(Materializer);

            return subscriber;
        }
    }
}