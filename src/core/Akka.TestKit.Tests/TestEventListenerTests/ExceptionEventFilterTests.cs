//-----------------------------------------------------------------------
// <copyright file="ExceptionEventFilterTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.Tests.Xunit2.TestEventListenerTests;
using Xunit;
using Xunit.Sdk;

namespace Akka.Testkit.Tests.TestEventListenerTests
{
    public class ExceptionEventFilterTests : EventFilterTestBase
    {
        public ExceptionEventFilterTests()
            : base("akka.logLevel=ERROR")
        {
        }
        public class SomeException : Exception { }

        protected override void SendRawLogEventMessage(object message)
        {
            Sys.EventStream.Publish(new Error(null, nameof(ExceptionEventFilterTests), GetType(), message));
        }

        [Fact]
        public void SingleExceptionIsIntercepted()
        {
            EventFilter.Exception<SomeException>()
                .ExpectOne(() => Log.Error(new SomeException(), "whatever"));
            ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void CanInterceptMessagesWhenStartIsSpecified()
        {
            EventFilter.Exception<SomeException>(start: "what")
                .ExpectOne(() => Log.Error(new SomeException(), "whatever"));
            ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void DoNotInterceptMessagesWhenStartDoesNotMatch()
        {
            EventFilter.Exception<SomeException>(start: "this is clearly not in message");
            Log.Error(new SomeException(), "whatever");
            ExpectMsg<Error>(err => (string)err.Message == "whatever");
        }

        [Fact]
        public void CanInterceptMessagesWhenMessageIsSpecified()
        {
            EventFilter.Exception<SomeException>(message: "whatever")
                .ExpectOne(() => Log.Error(new SomeException(), "whatever"));
            ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void DoNotInterceptMessagesWhenMessageDoesNotMatch()
        {
            EventFilter.Exception<SomeException>(message: "this is clearly not the message");
            Log.Error(new SomeException(), "whatever");
            ExpectMsg<Error>(err => (string)err.Message == "whatever");
        }

        [Fact]
        public void CanInterceptMessagesWhenContainsIsSpecified()
        {
            EventFilter.Exception<SomeException>(contains: "ate")
                .ExpectOne(() => Log.Error(new SomeException(), "whatever"));
            ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void DoNotInterceptMessagesWhenContainsDoesNotMatch()
        {
            EventFilter.Exception<SomeException>(contains: "this is clearly not in the message");
            Log.Error(new SomeException(), "whatever");
            ExpectMsg<Error>(err => (string)err.Message == "whatever");
        }


        [Fact]
        public void CanInterceptMessagesWhenSourceIsSpecified()
        {
            EventFilter.Exception<SomeException>(source: LogSource.Create(this, Sys).Source)
                .ExpectOne(() =>
            {
                Log.Error(new SomeException(), "whatever");
            });
            ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void DoNotInterceptMessagesWhenSourceDoesNotMatch()
        {
            EventFilter.Exception<SomeException>(source: "this is clearly not the source");
            Log.Error(new SomeException(), "whatever");
            ExpectMsg<Error>(err => (string)err.Message == "whatever");
        }


        [Fact]
        public void SpecifiedNumbersOfExceptionsCanBeIntercepted()
        {
            EventFilter.Exception<SomeException>()
                .Expect(2, () =>
                {
                    Log.Error(new SomeException(), "whatever");
                    Log.Error(new SomeException(), "whatever");
                });
            ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void ShouldFailIfMoreExceptionsThenSpecifiedAreLogged()
        {
            var exception = XAssert.Throws<TrueException>(() =>
                EventFilter.Exception<SomeException>().Expect(2, () =>
                {
                    Log.Error(new SomeException(), "whatever");
                    Log.Error(new SomeException(), "whatever");
                    Log.Error(new SomeException(), "whatever");
                }));
            Assert.Contains("1 message too many", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ShouldReportCorrectMessageCount()
        {
            var toSend = "Eric Cartman";
            var actor = ActorOf( ExceptionTestActor.Props() );

            EventFilter
                .Exception<InvalidOperationException>(source: actor.Path.ToString())
                // expecting 2 because the same exception is logged in PostRestart
                .Expect(2, () => actor.Tell( toSend ));
        }

        internal sealed class ExceptionTestActor : UntypedActor
        {
            private ILoggingAdapter Log { get; } = Context.GetLogger();

            protected override void PostRestart(Exception reason)
            {
                Log.Error(reason, "[PostRestart]");
                base.PostRestart(reason);
            }

            protected override void OnReceive( object message )
            {
                switch (message)
                {
                    case string msg:
                        throw new InvalidOperationException( "I'm sailing away. Set an open course" );

                    default:
                        Unhandled( message );
                        break;
                }
            }

            public static Props Props()
            {
                return Actor.Props.Create( () => new ExceptionTestActor() );
            }
        }
    }
}

