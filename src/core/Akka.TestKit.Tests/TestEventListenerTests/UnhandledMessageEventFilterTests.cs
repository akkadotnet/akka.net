//-----------------------------------------------------------------------
// <copyright file="UnhandledMessageEventFilterTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
            // Use a dedicated probe (not TestActor) to avoid mailbox collision:
            // ForwardAllEventsTestEventListener forwards ALL log events to TestActor,
            // so mixing EventStream.Subscribe(TestActor, ...) with ExpectMsgAsync<UnhandledMessage>()
            // causes type mismatches when Warning/Debug log events arrive first.
            var probe = CreateTestProbe();
            Sys.EventStream.Subscribe(probe, typeof(UnhandledMessage));
            try
            {
                await EventFilter
                    .Info()
                    .ExpectOneAsync(async () => {
                        _unhandledMessageActor.Tell("whatever");
                        // Wait on the isolated probe - guarantees message was processed
                        // and the Info log has been published before the filter checks.
                        await probe.ExpectMsgAsync<UnhandledMessage>();
                    });
            }
            finally
            {
                Sys.EventStream.Unsubscribe(probe, typeof(UnhandledMessage));
            }
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
