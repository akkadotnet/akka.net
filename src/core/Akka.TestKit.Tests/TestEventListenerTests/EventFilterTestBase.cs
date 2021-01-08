//-----------------------------------------------------------------------
// <copyright file="EventFilterTestBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Event;
using Akka.Testkit.Tests.TestEventListenerTests;

namespace Akka.TestKit.Tests.Xunit2.TestEventListenerTests
{
    public abstract class EventFilterTestBase : TestKit.Xunit2.TestKit
    {
        /// <summary>
        /// Used to signal that the test was successful and that we should ensure no more messages were logged
        /// </summary>
        protected bool TestSuccessful;

        protected EventFilterTestBase(string config)
            : base(@"akka.loggers = [""" + typeof(ForwardAllEventsTestEventListener).AssemblyQualifiedName + @"""], " + (string.IsNullOrEmpty(config) ? "" : config))
        {
            //We send a ForwardAllEventsTo containing message to the TestEventListenerToForwarder logger (configured as a logger above).
            //It should respond with an "OK" message when it has received the message.
            var initLoggerMessage = new ForwardAllEventsTestEventListener.ForwardAllEventsTo(TestActor);
            // ReSharper disable once DoNotCallOverridableMethodsInConstructor
            SendRawLogEventMessage(initLoggerMessage);
            ExpectMsg("OK");
            //From now on we know that all messages will be forwarded to TestActor
        }

        protected abstract void SendRawLogEventMessage(object message);

        protected override void AfterAll()
        {
            //After every test we make sure no uncatched messages have been logged
            if(TestSuccessful)
            {
                EnsureNoMoreLoggedMessages();
            }
            base.AfterAll();
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

