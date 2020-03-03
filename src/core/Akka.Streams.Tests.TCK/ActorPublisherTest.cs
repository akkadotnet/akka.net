//-----------------------------------------------------------------------
// <copyright file="ActorPublisherTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Actors;
using Akka.Util.Internal;
using Reactive.Streams;
using Akka.Actor;

namespace Akka.Streams.Tests.TCK
{
    class ActorPublisherTest : AkkaPublisherVerification<int?>
    {
        public override IPublisher<int?> CreatePublisher(long elements)
        {
            var actorRef =
                System.ActorOf(Props.Create(()=> new TestPublisher(elements)).WithDispatcher("akka.test.stream-dispatcher"));
            return ActorPublisher.Create<int?>(actorRef);
        }

        private sealed class TestPublisher : ActorPublisher<int?>
        {
            private sealed class Produce
            {
                public static Produce Instance { get; } = new Produce();

                private Produce()
                {
                    
                }
            }

            private sealed class Loop
            {
                public static Loop Instance { get; } = new Loop();

                private Loop()
                {

                }
            }

            private sealed class Complete
            {
                public static Complete Instance { get; } = new Complete();

                private Complete()
                {

                }
            }
            

            private readonly long _allElements;
            private int _current;
            private readonly int _count;

            public TestPublisher(long allElements)
            {
                _allElements = allElements;
                if (allElements == long.MaxValue)
                {
                    _current = 1;
                    _count = int.MaxValue;
                }
                else
                {
                    _current = 0;
                    _count = (int)(allElements);
                }
            }

            protected override bool Receive(object message) =>
                message.Match()
                    .With<Request>(_ => LoopDemand())
                    .With<Produce>(_ =>
                    {
                        if (TotalDemand > 0 && !IsCompleted && _current < _count)
                            OnNext(_current++);
                        else if (!IsCompleted && _current == _count)
                            OnComplete();
                        else if (IsCompleted)
                        {
                            //no-op
                        }
                    }).Default(_ =>
                    {
                        //no-op
                    })
                    .WasHandled;

            private void LoopDemand()
            {
                var loopUntil = Math.Min(100, TotalDemand);
                Enumerable.Range(1, (int) loopUntil).ForEach(_ => Self.Tell(Produce.Instance));
                if(loopUntil > 100)
                    Self.Tell(Loop.Instance);
            }
        }
    }
}
