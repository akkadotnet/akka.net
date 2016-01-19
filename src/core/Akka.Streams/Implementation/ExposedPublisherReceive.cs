using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.Streams.Implementation
{
    public abstract class ExposedPublisherReceive<T>
    {
        public readonly Receive ActiveReceive;
        public readonly Action<object> Unhandled;

        private readonly LinkedList<object> _stash = new LinkedList<object>();

        protected ExposedPublisherReceive(Receive activeReceive, Action<object> unhandled)
        {
            ActiveReceive = activeReceive;
            Unhandled = unhandled;
        }

        public abstract void ReceiveExposedPublisher(ExposedPublisher<T> publisher);

        public bool Apply(object message)
        {
            ExposedPublisher<T> publisher;
            if ((publisher = message as ExposedPublisher<T>) != null)
            {
                ReceiveExposedPublisher(publisher);
                if (_stash.Any())
                {
                    // we don't use sender() so this is allright
                    foreach (var msg in _stash)
                        if (!ActiveReceive(msg)) Unhandled(msg);
                }
            }
            else _stash.AddLast(message);

            return true;
        }
    }
}