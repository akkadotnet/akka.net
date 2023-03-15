// //-----------------------------------------------------------------------
// // <copyright file="UnhandledMessageEventFilterTestsBase.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit.TestActors;
using Xunit;

namespace Akka.TestKit.Tests.TestEventListenerTests
{
    public class UnhandledMessageEventFilterTests : EventFilterTestBase
    {
        private readonly IActorRef _unhandledMessageActor;

        public UnhandledMessageEventFilterTests() : base("akka.loglevel=INFO")
        {
            _unhandledMessageActor = Sys.ActorOf<UnhandledMessageActor>();
        }

        protected override void SendRawLogEventMessage(object message)
        {
            Sys.EventStream.Publish(new Error(null, "UnhandledMessageEventFilterTests", GetType(), message));
        }

        [Fact]
        public async Task Unhandled_message_should_produce_info_message()
        {
            await EventFilter
                .Info(new Regex("^Unhandled message from"))
                .ExpectOneAsync(() => {
                    _unhandledMessageActor.Tell("whatever");
                    return Task.CompletedTask;
                });
        }
        
        [Fact]
        public async Task Unhandled_message_should_not_produce_warn_and_error_message()
        {
            await EventFilter
                .Warning()
                .And
                .Error()
                .ExpectAsync(0, () => {
                    _unhandledMessageActor.Tell("whatever");
                    return Task.CompletedTask;
                });
        }
    }
}