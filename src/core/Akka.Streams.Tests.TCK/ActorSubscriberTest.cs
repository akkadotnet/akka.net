//-----------------------------------------------------------------------
// <copyright file="ActorSubscriberTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Streams.Actors;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK
{
    class ActorSubscriberOneByOneRequestTest : AkkaSubscriberBlackboxVerification<int?>
    {
        private sealed class StrategySubscriber : ActorSubscriber
        {
            public StrategySubscriber(IRequestStrategy requestStrategy)
            {
                RequestStrategy = requestStrategy;
            }

            protected override bool Receive(object message) => true;

            public override IRequestStrategy RequestStrategy { get; }
        }

        public override int? CreateElement(int element) => element;

        public override ISubscriber<int?> CreateSubscriber()
        {
            var props = Props.Create(() => new StrategySubscriber(OneByOneRequestStrategy.Instance));
            return ActorSubscriber.Create<int?>(System.ActorOf(props.WithDispatcher("akka.test.stream-dispatcher")));
        }
    }
}
