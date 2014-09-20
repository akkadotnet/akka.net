using System.Collections.Generic;
using System.Runtime.Remoting.Contexts;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.Internal;

namespace Akka.Testkit.Tests.TestEventListenerTests
{
    public class ForwardAllEventsTestEventListener : TestEventListener
    {
        private ActorRef _forwarder;

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
            private readonly ActorRef _forwarder;

            public ForwardAllEventsTo(ActorRef forwarder)
            {
                _forwarder = forwarder;
            }

            public ActorRef Forwarder { get { return _forwarder; } }
        }
    }

}