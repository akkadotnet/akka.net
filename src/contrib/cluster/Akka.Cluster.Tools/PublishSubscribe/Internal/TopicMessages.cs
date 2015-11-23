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
    public struct Bucket
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
            Versions = versions ?? new Dictionary<Address, long>(0);
        }
    }

    [Serializable]
    public sealed class Delta : IDistributedPubSubMessage
    {
        public readonly Bucket[] Buckets;

        public Delta(Bucket[] buckets)
        {
            Buckets = buckets ?? new Bucket[0];
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
        public readonly Distributed.SubscribeAck Ack;
        public readonly IActorRef Subscriber;

        public Subscribed(Distributed.SubscribeAck ack, IActorRef subscriber)
        {
            Ack = ack;
            Subscriber = subscriber;
        }
    }

    [Serializable]
    public sealed class Unsubscribed
    {
        public readonly Distributed.UnsubscribeAck Ack;
        public readonly IActorRef Subscriber;

        public Unsubscribed(Distributed.UnsubscribeAck ack, IActorRef subscriber)
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