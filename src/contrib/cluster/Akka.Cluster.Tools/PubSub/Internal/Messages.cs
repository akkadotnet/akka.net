using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Routing;
using Akka.Util.Internal.Collections;

namespace Akka.Cluster.Tools.PubSub.Internal
{
    [Serializable]
    public sealed class Prune
    {
        public static readonly Prune Instance = new Prune();
        private Prune() { }
    }

    // Only for testing purposes, to poll/await replication
    public sealed class Count
    {
        public static readonly Count Instance = new Count();

        private Count() { }
    }

    [Serializable]
    public sealed class Bucket
    {
        public readonly Address Owner;
        public readonly long Version;
        public readonly IImmutableMap<string, ValueHolder> Content;

        public Bucket(Address owner, long version, IImmutableMap<string, ValueHolder> content)
        {
            Owner = owner;
            Version = version;
            Content = content;
        }
    }

    [Serializable]
    public sealed class ValueHolder
    {
        public readonly long Version;
        public readonly IActorRef Ref;

        [NonSerialized]
        private Routee _routee;

        public ValueHolder(long version, IActorRef @ref)
        {
            Version = version;
            Ref = @ref;
        }

        public Routee Routee { get { return _routee ?? (_routee = Ref != null ? new ActorRefRoutee(Ref) : null); } }
    }

    [Serializable]
    public sealed class Status : IDistributedPubSubMessage
    {
        public readonly IDictionary<Address, long> Versions;

        public Status(IDictionary<Address, long> versions)
        {
            Versions = versions;
        }
    }

    [Serializable]
    public sealed class Delta : IDistributedPubSubMessage
    {
        public readonly Bucket[] Buckets;

        public Delta(Bucket[] buckets)
        {
            Buckets = buckets;
        }
    }

    [Serializable]
    public sealed class GossipTick
    {
        public static readonly GossipTick Instance = new GossipTick();

        private GossipTick() { }
    }

    [Serializable]
    public sealed class RegisterTopic
    {
        public readonly IActorRef TopicRef;

        public RegisterTopic(IActorRef topicRef)
        {
            TopicRef = topicRef;
        }
    }

    [Serializable]
    public sealed class Subscribed
    {
        public readonly DistributedPubSubMediator.SubscribeAck Ack;
        public readonly IActorRef Subscriber;

        public Subscribed(DistributedPubSubMediator.SubscribeAck ack, IActorRef subscriber)
        {
            Ack = ack;
            Subscriber = subscriber;
        }
    }

    [Serializable]
    public sealed class Unsubscribed
    {
        public readonly DistributedPubSubMediator.UnsubscribeAck Ack;
        public readonly IActorRef Subscriber;

        public Unsubscribed(DistributedPubSubMediator.UnsubscribeAck ack, IActorRef subscriber)
        {
            Ack = ack;
            Subscriber = subscriber;
        }
    }

    [Serializable]
    public sealed class SendToOneSubscriber
    {
        public readonly object Message;

        public SendToOneSubscriber(object message)
        {
            Message = message;
        }
    }

    [Serializable]
    public sealed class MediatorRouterEnvelope : RouterEnvelope
    {
        public MediatorRouterEnvelope(object message) : base(message) { }
    }
}