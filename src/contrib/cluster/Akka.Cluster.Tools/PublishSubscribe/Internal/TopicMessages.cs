﻿//-----------------------------------------------------------------------
// <copyright file="TopicMessages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Event;
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
    internal class Bucket : IEquatable<Bucket>
    {
        public readonly Address Owner;
        public readonly long Version;
        public readonly IImmutableDictionary<string, ValueHolder> Content;

        public Bucket(Address owner) : this(owner, 0L, ImmutableDictionary<string, ValueHolder>.Empty)
        {
        }

        public Bucket(Address owner, long version, IImmutableDictionary<string, ValueHolder> content)
        {
            Owner = owner;
            Version = version;
            Content = content;
        }

        public bool Equals(Bucket other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;

            return Equals(Owner, other.Owner)
                   && Equals(Version, other.Version)
                   && Content.SequenceEqual(other.Content);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Bucket);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Owner != null ? Owner.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Version.GetHashCode();
                hashCode = (hashCode * 397) ^ (Content != null ? Content.GetHashCode() : 0);
                return hashCode;
            }
        }
    }

    [Serializable]
    internal sealed class ValueHolder : IEquatable<ValueHolder>
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

        public bool Equals(ValueHolder other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return Equals(Version, other.Version) &&
                   Equals(Ref, other.Ref);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ValueHolder);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Version.GetHashCode();
                hashCode = (hashCode * 397) ^ (Ref != null ? Ref.GetHashCode() : 0);
                return hashCode;
            }
        }
    }

    [Serializable]
    internal sealed class Status : IDistributedPubSubMessage, IDeadLetterSuppression
    {
        public Status(IDictionary<Address, long> versions, bool isReplyToStatus)
        {
            Versions = versions ?? new Dictionary<Address, long>(0);
            IsReplyToStatus = isReplyToStatus;
        }

        public IDictionary<Address, long> Versions { get; }

        public bool IsReplyToStatus { get; }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(obj, null)) return false;
            if (ReferenceEquals(obj, this)) return true;

            var other = obj as Status;
            if (other == null)
                return false;

            return Versions.SequenceEqual(other.Versions) 
                && IsReplyToStatus.Equals(other.IsReplyToStatus);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = 13;
                foreach (var v in Versions.Values)
                {
                    hashCode = hashCode * 17 + v.GetHashCode();
                }

                hashCode = hashCode * 17 + IsReplyToStatus.GetHashCode();

                return hashCode;
            }
        }
    }

    [Serializable]
    internal sealed class Delta : IDistributedPubSubMessage, IEquatable<Delta>, IDeadLetterSuppression
    {
        public readonly Bucket[] Buckets;

        public Delta(Bucket[] buckets)
        {
            Buckets = buckets ?? new Bucket[0];
        }

        public bool Equals(Delta other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;

            return Buckets.SequenceEqual(other.Buckets);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Delta);
        }

        public override int GetHashCode()
        {
            return Buckets != null ? Buckets.GetHashCode() : 0;
        }
    }

    // Only for testing purposes, to verify replication
    [Serializable]
    internal sealed class DeltaCount
    {
        public static readonly DeltaCount Instance = new DeltaCount();

        private DeltaCount() { }
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

    /// <summary>
    /// Messages used to encode protocol to make sure that we do not send Subscribe/Unsubscribe message to
    /// child (mediator -&gt; topic, topic -&gt; group) during a period of transition. Protects from situations like:
    /// Sending Subscribe/Unsubscribe message to child actor after child has been terminated
    /// but Terminate message did not yet arrive to parent.
    /// Sending Subscribe/Unsubscribe message to child actor that has Prune message queued and pruneDeadline set.
    /// In both of those situation parent actor still thinks that child actor is alive and forwards messages to it resulting in lost ACKs.
    /// </summary>
    internal interface IChildActorTerminationProtocol
    {
    }

    /// <summary>
    /// Passivate-like message sent from child to parent, used to signal that sender has no subscribers and no child actors.
    /// </summary>
    internal class NoMoreSubscribers : IChildActorTerminationProtocol
    {
        public static NoMoreSubscribers Instance = new NoMoreSubscribers();
        private NoMoreSubscribers()
        {
        }
    }

    /// <summary>
    /// Sent from parent to child actor to signalize that messages are being buffered. When received by child actor
    /// if no <see cref="Subscribe"/> message has been received after sending <see cref="NoMoreSubscribers"/> message child actor will stop itself.
    /// </summary>
    internal class TerminateRequest : IChildActorTerminationProtocol
    {
        public static TerminateRequest Instance = new TerminateRequest();
        private TerminateRequest()
        {
        }
    }

    /// <summary>
    /// Sent from child to parent actor as response to <see cref="TerminateRequest"/> in case <see cref="Subscribe"/> message arrived
    /// after sending <see cref="NoMoreSubscribers"/> but before receiving <see cref="TerminateRequest"/>.
    /// When received by the parent buffered messages will be forwarded to child actor for processing.
    /// </summary>
    internal class NewSubscriberArrived : IChildActorTerminationProtocol
    {
        public static NewSubscriberArrived Instance = new NewSubscriberArrived();
        private NewSubscriberArrived()
        {
        }
    }

    [Serializable]
    internal sealed class MediatorRouterEnvelope : RouterEnvelope
    {
        public MediatorRouterEnvelope(object message) : base(message) { }
    }
}