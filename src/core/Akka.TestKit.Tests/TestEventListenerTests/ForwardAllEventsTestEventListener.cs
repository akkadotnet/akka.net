//-----------------------------------------------------------------------
// <copyright file="ForwardAllEventsTestEventListener.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;

namespace Akka.TestKit.Tests.TestEventListenerTests
{
    public class ForwardAllEventsTestEventListener : TestEventListener
    {
        private IActorRef _forwarder;

        protected override void Print(LogEvent m)
        {           
            if(m.Message is ForwardAllEventsTo to)
            {
                // EventFilterTestBase retries this message until the logger has subscribed, so several can arrive;
                // reply once per forwarder, or a late extra "OK" is left in the TestActor's queue
                if (!Equals(_forwarder, to.Forwarder))
                {
                    _forwarder = to.Forwarder;
                    _forwarder.Tell("OK");
                }
            }
            else if(_forwarder != null)
            {
                _forwarder.Forward(m);
            }
            else
            {
                base.Print(m);
            }
        }

        public class ForwardAllEventsTo
        {
            public ForwardAllEventsTo(IActorRef forwarder)
            {
                Forwarder = forwarder;
            }

            public IActorRef Forwarder { get; }
        }
    }

}

