using System.Collections.Generic;
using Akka.Actor;
using Akka.Utils;

namespace Akka.Event
{
    /// <summary>
    /// INTERNAL API.
    /// 
    /// Watchers of remote actor references register themselves as subscribers of
    /// <see cref="AddressTerminated"/> notifications. Remote and cluster death watchers
    /// publish <see cref="AddressTerminated"/> when a remote system is deemed dead.
    /// </summary>
    internal sealed class AddressTerminatedTopic
    {
        private readonly AtomicReference<HashSet<ActorRef>> _subscribers = new AtomicReference<HashSet<ActorRef>>();

        public void Subscribe(ActorRef subscriber)
        {
            while (true)
            {
                var current = _subscribers;
                if (!_subscribers.CompareAndSet(current, new HashSet<ActorRef>(current.Value) {subscriber}))
                    continue;
                break;
            }
        }

        public void Unsubscribe(ActorRef subscriber)
        {
            while (true)
            {
                var current = _subscribers;
                var newSet = new HashSet<ActorRef>(_subscribers.Value);
                newSet.Remove(subscriber);
                if (!_subscribers.CompareAndSet(current, newSet))
                    continue;
                break;
            }
        }

        public void Publish(AddressTerminated msg)
        {
            foreach (var subscriber in _subscribers.Value)
            {
                subscriber.Tell(msg, ActorRef.NoSender);
            }
        }
    }
}
