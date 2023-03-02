//-----------------------------------------------------------------------
// <copyright file="DeadLettersEventFilterTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit.TestActors;
using Xunit;

namespace Akka.TestKit.Tests.TestEventListenerTests
{
    public abstract class DeadLettersEventFilterTestsBase : EventFilterTestBase
    {
        private readonly IActorRef _deadActor;

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
        public async Task Should_be_able_to_filter_dead_letters()
        {
            var eventFilter = CreateTestingEventFilter();
            await eventFilter.DeadLetter().ExpectOneAsync(async () =>
            {
                _deadActor.Tell("whatever");
            });
        }
        
        [Fact]
        public async Task Should_check_properly_type_parameters()
        {
            await EventFilter.DeadLetter<int>().And.DeadLetter<double>().ExpectAsync(2, async () =>
            {
                _deadActor.Tell(5);
                _deadActor.Tell(1.2);
            });
        }

        [Fact]
        public async Task Should_check_properly_type_parameters_when_one_of_them_string()
        {
            await EventFilter.DeadLetter<string>().And.DeadLetter<double>().ExpectAsync(2, async () =>
            {
                _deadActor.Tell("asd");
                _deadActor.Tell(1.2);
            });
        }
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

