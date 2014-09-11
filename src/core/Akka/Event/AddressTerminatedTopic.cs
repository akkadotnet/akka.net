using System.Collections.Generic;
using Akka.Actor;
using Akka.Util;

namespace Akka.Event
{
    internal sealed class AddressTerminatedTopicProvider : ExtensionIdProvider<AddressTerminatedTopic>
    {
        public override AddressTerminatedTopic CreateExtension(ExtendedActorSystem system)
        {
            return new AddressTerminatedTopic();
        }
    }

    /// <summary>
    /// INTERNAL API.
    /// 
    /// Watchers of remote actor references register themselves as subscribers of
    /// <see cref="AddressTerminated"/> notifications. Remote and cluster death watchers
    /// publish <see cref="AddressTerminated"/> when a remote system is deemed dead.
    /// </summary>
    internal sealed class AddressTerminatedTopic : IExtension
    {
        private readonly AtomicReference<HashSet<ActorRef>> _subscribers = new AtomicReference<HashSet<ActorRef>>(new HashSet<ActorRef>());

        public static AddressTerminatedTopic Get(ActorSystem system)
        {
            return system.WithExtension<AddressTerminatedTopic>(typeof(AddressTerminatedTopicProvider));
        }

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
