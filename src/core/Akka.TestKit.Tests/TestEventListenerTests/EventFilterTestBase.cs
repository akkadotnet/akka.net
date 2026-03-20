//-----------------------------------------------------------------------
// <copyright file="EventFilterTestBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Event;
using Xunit;

namespace Akka.TestKit.Tests.TestEventListenerTests
{
    public abstract class EventFilterTestBase : TestKit.Xunit.TestKit, IAsyncLifetime
    {
        /// <summary>
        /// Used to signal that the test was successful and that we should ensure no more messages were logged
        /// </summary>
        protected bool TestSuccessful;

        protected EventFilterTestBase(string config)
            : base(@"akka.loggers = [""" + typeof(ForwardAllEventsTestEventListener).AssemblyQualifiedName + @"""], " + (string.IsNullOrEmpty(config) ? "" : config))
        {
        }

        public async ValueTask InitializeAsync()
        {
            //We send a ForwardAllEventsTo containing message to the TestEventListenerToForwarder logger (configured as a logger above).
            //It should respond with an "OK" message when it has received the message.
            var initLoggerMessage = new ForwardAllEventsTestEventListener.ForwardAllEventsTo(TestActor);
            
            // Retry logger initialization to handle race conditions where logging system isn't ready yet
            await AwaitAssertAsync(async () =>
            {
                SendRawLogEventMessage(initLoggerMessage);
                await ExpectMsgAsync("OK", TimeSpan.FromSeconds(1));
            }, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200));
            
            //From now on we know that all messages will be forwarded to TestActor
        }

        public ValueTask DisposeAsync()
        {
            return new ValueTask(Task.CompletedTask);
        }

        protected abstract void SendRawLogEventMessage(object message);

        protected override void AfterAll()
        {
            //After every test we make sure no uncatched messages have been logged
            Exception exception = null;
            if(TestSuccessful)
            {
                try
                {
                    EnsureNoMoreLoggedMessages();
                }
                catch (Exception e)
                {
                    exception = e;
                }
            }
            base.AfterAll();
            if (exception is { })
                throw exception;
        }

        private void EnsureNoMoreLoggedMessages()
        {
            //We log a Finished message. When it arrives to TestActor we know no other message has been logged.
            //If we receive something else it means another message was logged, and ExpectMsg will fail
            const string message = "<<Finished>>";
            SendRawLogEventMessage(message);
            ExpectMsg<LogEvent>(err => (string) err.Message == message,hint: "message to be \"" + message + "\"");
        }

    }
}

