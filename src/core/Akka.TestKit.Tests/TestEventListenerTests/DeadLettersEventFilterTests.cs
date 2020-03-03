//-----------------------------------------------------------------------
// <copyright file="DeadLettersEventFilterTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using Akka.TestKit.TestActors;
using Xunit;

namespace Akka.TestKit.Tests.Xunit2.TestEventListenerTests
{
    public abstract class DeadLettersEventFilterTestsBase : EventFilterTestBase
    {
        private readonly IActorRef _deadActor;

        // ReSharper disable ConvertToLambdaExpression
        protected DeadLettersEventFilterTestsBase() : base("akka.loglevel=ERROR")
        {
            _deadActor = Sys.ActorOf(BlackHoleActor.Props, "dead-actor");
            Watch(_deadActor);
            Sys.Stop(_deadActor);
            ExpectTerminated(_deadActor);
        }

        protected override void SendRawLogEventMessage(object message)
        {
            Sys.EventStream.Publish(new Error(null, "DeadLettersEventFilterTests", GetType(), message));
        }

        protected abstract EventFilterFactory CreateTestingEventFilter();

        [Fact]
        public void Should_be_able_to_filter_dead_letters()
        {
            var eventFilter = CreateTestingEventFilter();
            eventFilter.DeadLetter().ExpectOne(() =>
            {
                _deadActor.Tell("whatever");
            });
        }


        // ReSharper restore ConvertToLambdaExpression
    }

    public class DeadLettersEventFilterTests : DeadLettersEventFilterTestsBase
    {
        protected override EventFilterFactory CreateTestingEventFilter()
        {
            return EventFilter;
        }
    }

    public class DeadLettersCustomEventFilterTests : DeadLettersEventFilterTestsBase
    {
        protected override EventFilterFactory CreateTestingEventFilter()
        {
            return CreateEventFilter(Sys);
        }
    }
}

