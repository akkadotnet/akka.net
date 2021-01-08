//-----------------------------------------------------------------------
// <copyright file="ForwardAllEventsTestEventListener.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using Akka.TestKit;

namespace Akka.Testkit.Tests.TestEventListenerTests
{
    public class ForwardAllEventsTestEventListener : TestEventListener
    {
        private IActorRef _forwarder;

        protected override void Print(LogEvent m)
        {           
            if(m.Message is ForwardAllEventsTo)
            {
                _forwarder = ((ForwardAllEventsTo)m.Message).Forwarder;
                _forwarder.Tell("OK");
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
            private readonly IActorRef _forwarder;

            public ForwardAllEventsTo(IActorRef forwarder)
            {
                _forwarder = forwarder;
            }

            public IActorRef Forwarder { get { return _forwarder; } }
        }
    }

}

