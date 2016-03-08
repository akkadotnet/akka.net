//-----------------------------------------------------------------------
// <copyright file="TopicMessages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Routing;

namespace Akka.Cluster.Tools.PublishSubscribe.Internal
{
    [Serializable]
    internal sealed class Prune
    {
        public static readonly Prune Instance = new Prune();
        private Prune() { }
    }

    // Only for testing purposes, to poll/await replication
    internal sealed class Count
    {
        public static readonly Count Instance = new Count();
        private Count() { }
    }

    [Serializable]
    internal struct Bucket
    {
        public readonly Address Owner;
        public readonly long Version;
        public readonly IImmutableDictionary<string, ValueHolder> Content;

        public Bucket(Address owner) : this(owner, 0L, ImmutableDictionary<string, ValueHolder>.Empty)
        {
        }

        public Bucket(Address owner, long version, IImmutableDictionary<string, ValueHolder> content) : this()
        {
            Owner = owner;
            Version = version;
            Content = content;
        }
    }

    [Serializable]
    internal sealed class ValueHolder
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
    internal sealed class Status : IDistributedPubSubMessage
    {
        public readonly IDictionary<Address, long> Versions;

        public Status(IDictionary<Address, long> versions)
        {
            Versions = versions ?? new Dictionary<Address, long>(0);
        }
    }

    [Serializable]
    internal sealed class Delta : IDistributedPubSubMessage
    {
        public readonly Bucket[] Buckets;

        public Delta(Bucket[] buckets)
        {
            Buckets = buckets ?? new Bucket[0];
        }
    }

    [Serializable]
    internal sealed class GossipTick
    {
        public static readonly GossipTick Instance = new GossipTick();

        private GossipTick() { }
    }

    [Serializable]
    internal sealed class RegisterTopic
    {
        public readonly IActorRef TopicRef;

        public RegisterTopic(IActorRef topicRef)
        {
            TopicRef = topicRef;
        }
    }

    [Serializable]
    internal sealed class Subscribed
    {
        public readonly SubscribeAck Ack;
        public readonly IActorRef Subscriber;

        public Subscribed(SubscribeAck ack, IActorRef subscriber)
        {
            Ack = ack;
            Subscriber = subscriber;
        }
    }

    [Serializable]
    internal sealed class Unsubscribed
    {
        public readonly UnsubscribeAck Ack;
        public readonly IActorRef Subscriber;

        public Unsubscribed(UnsubscribeAck ack, IActorRef subscriber)
        {
            Ack = ack;
            Subscriber = subscriber;
        }
    }

    [Serializable]
    internal sealed class SendToOneSubscriber
    {
        public readonly object Message;

        public SendToOneSubscriber(object message)
        {
            Message = message;
        }
    }

    [Serializable]
    internal sealed class MediatorRouterEnvelope : RouterEnvelope
    {
        public MediatorRouterEnvelope(object message) : base(message) { }
    }
}