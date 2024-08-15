// -----------------------------------------------------------------------
//  <copyright file="ActorPublisherTest.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Streams.Actors;
using Akka.Util.Internal;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK;

internal class ActorPublisherTest : AkkaPublisherVerification<int?>
{
    public override IPublisher<int?> CreatePublisher(long elements)
    {
        var actorRef =
            System.ActorOf(
                Props.Create(() => new TestPublisher(elements)).WithDispatcher("akka.test.stream-dispatcher"));
        return ActorPublisher.Create<int?>(actorRef);
    }

    private sealed class TestPublisher : ActorPublisher<int?>
    {
        private readonly long _allElements;
        private readonly int _count;
        private int _current;

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
                _count = (int)allElements;
            }
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case Request _:
                    LoopDemand();
                    return true;

                case Produce _:
                    if (TotalDemand > 0 && !IsCompleted && _current < _count)
                    {
                        OnNext(_current++);
                    }
                    else if (!IsCompleted && _current == _count)
                    {
                        OnComplete();
                    }
                    else if (IsCompleted)
                    {
                        //no-op
                    }

                    return true;

                default:
                    //no-op
                    return true;
            }
        }

        private void LoopDemand()
        {
            var loopUntil = Math.Min(100, TotalDemand);
            Enumerable.Range(1, (int)loopUntil).ForEach(_ => Self.Tell(Produce.Instance));
            if (loopUntil > 100)
                Self.Tell(Loop.Instance);
        }

        private sealed class Produce
        {
            private Produce()
            {
            }

            public static Produce Instance { get; } = new();
        }

        private sealed class Loop
        {
            private Loop()
            {
            }

            public static Loop Instance { get; } = new();
        }

        private sealed class Complete
        {
            private Complete()
            {
            }

            public static Complete Instance { get; } = new();
        }
    }
}