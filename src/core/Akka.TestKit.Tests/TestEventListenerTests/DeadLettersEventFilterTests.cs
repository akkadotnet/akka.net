//-----------------------------------------------------------------------
// <copyright file="DeadLettersEventFilterTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using Akka.TestKit.TestActors;
using Xunit;

namespace Akka.Testkit.Tests.TestEventListenerTests
{
    public class DeadLettersEventFilterTests : EventFilterTestBase
    {
        private readonly IActorRef _deadActor;
        // ReSharper disable ConvertToLambdaExpression
        public DeadLettersEventFilterTests() : base("akka.loglevel=ERROR")
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

        [Fact]
        public void ShouldBeAbleToFilterDeadLetters()
        {
            EventFilter.DeadLetter().ExpectOne(() =>
            {
                _deadActor.Tell("whatever");
            });
        }


        // ReSharper restore ConvertToLambdaExpression
    }
}
