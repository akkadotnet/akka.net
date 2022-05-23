//-----------------------------------------------------------------------
// <copyright file="ExceptionEventFilterTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using FluentAssertions;
using Xunit;
using Xunit.Sdk;
using static FluentAssertions.FluentActions;

namespace Akka.TestKit.Tests.TestEventListenerTests
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
        public async Task SingleExceptionIsIntercepted()
        {
            await EventFilter.Exception<SomeException>()
                .ExpectOneAsync(() => Log.Error(new SomeException(), "whatever"));
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public async Task CanInterceptMessagesWhenStartIsSpecified()
        {
            await EventFilter.Exception<SomeException>(start: "what")
                .ExpectOneAsync(() => Log.Error(new SomeException(), "whatever"));
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public async Task DoNotInterceptMessagesWhenStartDoesNotMatch()
        {
            EventFilter.Exception<SomeException>(start: "this is clearly not in message");
            Log.Error(new SomeException(), "whatever");
            await ExpectMsgAsync<Error>(err => (string)err.Message == "whatever");
        }

        [Fact]
        public async Task CanInterceptMessagesWhenMessageIsSpecified()
        {
            await EventFilter.Exception<SomeException>(message: "whatever")
                .ExpectOneAsync(() => Log.Error(new SomeException(), "whatever"));
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public async Task DoNotInterceptMessagesWhenMessageDoesNotMatch()
        {
            EventFilter.Exception<SomeException>(message: "this is clearly not the message");
            Log.Error(new SomeException(), "whatever");
            await ExpectMsgAsync<Error>(err => (string)err.Message == "whatever");
        }

        [Fact]
        public async Task CanInterceptMessagesWhenContainsIsSpecified()
        {
            await EventFilter.Exception<SomeException>(contains: "ate")
                .ExpectOneAsync(() => Log.Error(new SomeException(), "whatever"));
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public async Task DoNotInterceptMessagesWhenContainsDoesNotMatch()
        {
            EventFilter.Exception<SomeException>(contains: "this is clearly not in the message");
            Log.Error(new SomeException(), "whatever");
            await ExpectMsgAsync<Error>(err => (string)err.Message == "whatever");
        }


        [Fact]
        public async Task CanInterceptMessagesWhenSourceIsSpecified()
        {
            await EventFilter.Exception<SomeException>(source: LogSource.Create(this, Sys).Source)
                .ExpectOneAsync(() => Log.Error(new SomeException(), "whatever"));
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public async Task DoNotInterceptMessagesWhenSourceDoesNotMatch()
        {
            EventFilter.Exception<SomeException>(source: "this is clearly not the source");
            Log.Error(new SomeException(), "whatever");
            await ExpectMsgAsync<Error>(err => (string)err.Message == "whatever");
        }


        [Fact]
        public async Task SpecifiedNumbersOfExceptionsCanBeIntercepted()
        {
            await EventFilter.Exception<SomeException>()
                .ExpectAsync(2, () =>
                {
                    Log.Error(new SomeException(), "whatever");
                    Log.Error(new SomeException(), "whatever");
                });
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public async Task ShouldFailIfMoreExceptionsThenSpecifiedAreLogged()
        {
            await Awaiting(async () =>
                {
                    await EventFilter.Exception<SomeException>().ExpectAsync(2, () =>
                    {
                        Log.Error(new SomeException(), "whatever");
                        Log.Error(new SomeException(), "whatever");
                        Log.Error(new SomeException(), "whatever");
                    });                    
                })
                .Should().ThrowAsync<TrueException>().WithMessage("Received 1 message too many.*");
        }

        [Fact]
        public async Task ShouldReportCorrectMessageCount()
        {
            var toSend = "Eric Cartman";
            var actor = ActorOf( ExceptionTestActor.Props() );

            await EventFilter
                .Exception<InvalidOperationException>(source: actor.Path.ToString())
                // expecting 2 because the same exception is logged in PostRestart
                .ExpectAsync(2, () => actor.Tell( toSend ));
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
                    case string _:
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

