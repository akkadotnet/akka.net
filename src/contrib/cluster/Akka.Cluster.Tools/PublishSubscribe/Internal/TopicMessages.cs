//-----------------------------------------------------------------------
// <copyright file="TopicMessages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class Prune
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static Prune Instance { get; } = new Prune();
        private Prune() { }
    }

    // Only for testing purposes, to poll/await replication
    /// <summary>
    /// TBD
    /// </summary>
    internal sealed class Count
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static Count Instance { get; } = new Count();
        private Count() { }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal sealed class CountSubscribers
    {
        public string Topic { get; }

        public CountSubscribers(string topic)
        {
            Topic = topic;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal class Bucket : IEquatable<Bucket>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public Address Owner { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public long Version { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public IImmutableDictionary<string, ValueHolder> Content { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="owner">TBD</param>
        public Bucket(Address owner) : this(owner, 0L, ImmutableDictionary<string, ValueHolder>.Empty)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="owner">TBD</param>
        /// <param name="version">TBD</param>
        /// <param name="content">TBD</param>
        public Bucket(Address owner, long version, IImmutableDictionary<string, ValueHolder> content)
        {
            Owner = owner;
            Version = version;
            Content = content;
        }

        /// <inheritdoc/>
        public bool Equals(Bucket other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;

            return Equals(Owner, other.Owner)
                   && Equals(Version, other.Version)
                   && Content.SequenceEqual(other.Content);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as Bucket);
        }

        /// <inheritdoc/>
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

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class ValueHolder : IEquatable<ValueHolder>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public long Version { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public IActorRef Ref { get; }

        [NonSerialized]
        private Routee _routee;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="version">TBD</param>
        /// <param name="ref">TBD</param>
        public ValueHolder(long version, IActorRef @ref)
        {
            Version = version;
            Ref = @ref;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Routee Routee { get { return _routee ?? (_routee = Ref != null ? new ActorRefRoutee(Ref) : null); } }

        /// <inheritdoc/>
        public bool Equals(ValueHolder other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return Equals(Version, other.Version) &&
                   Equals(Ref, other.Ref);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as ValueHolder);
        }

        /// <inheritdoc/>
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

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class Status : IDistributedPubSubMessage, IDeadLetterSuppression
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="versions">TBD</param>
        /// <param name="isReplyToStatus">TBD</param>
        public Status(IDictionary<Address, long> versions, bool isReplyToStatus)
        {
            Versions = versions ?? new Dictionary<Address, long>(0);
            IsReplyToStatus = isReplyToStatus;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IDictionary<Address, long> Versions { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsReplyToStatus { get; }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
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

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class Delta : IDistributedPubSubMessage, IEquatable<Delta>, IDeadLetterSuppression
    {
        /// <summary>
        /// TBD
        /// </summary>
        public Bucket[] Buckets { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="buckets">TBD</param>
        public Delta(Bucket[] buckets)
        {
            Buckets = buckets ?? new Bucket[0];
        }

        /// <inheritdoc/>
        public bool Equals(Delta other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;

            return Buckets.SequenceEqual(other.Buckets);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as Delta);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return Buckets != null ? Buckets.GetHashCode() : 0;
        }
    }

    // Only for testing purposes, to verify replication
    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class DeltaCount
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly DeltaCount Instance = new DeltaCount();

        private DeltaCount() { }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class GossipTick
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static GossipTick Instance { get; } = new GossipTick();

        private GossipTick() { }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class RegisterTopic
    {
        /// <summary>
        /// TBD
        /// </summary>
        public IActorRef TopicRef { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="topicRef">TBD</param>
        public RegisterTopic(IActorRef topicRef)
        {
            TopicRef = topicRef;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class Subscribed
    {
        /// <summary>
        /// TBD
        /// </summary>
        public SubscribeAck Ack { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public IActorRef Subscriber { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ack">TBD</param>
        /// <param name="subscriber">TBD</param>
        public Subscribed(SubscribeAck ack, IActorRef subscriber)
        {
            Ack = ack;
            Subscriber = subscriber;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class Unsubscribed
    {
        /// <summary>
        /// TBD
        /// </summary>
        public UnsubscribeAck Ack { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public IActorRef Subscriber { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ack">TBD</param>
        /// <param name="subscriber">TBD</param>
        public Unsubscribed(UnsubscribeAck ack, IActorRef subscriber)
        {
            Ack = ack;
            Subscriber = subscriber;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class SendToOneSubscriber
    {
        /// <summary>
        /// TBD
        /// </summary>
        public object Message { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        public SendToOneSubscriber(object message)
        {
            Message = message;
        }

        private bool Equals(SendToOneSubscriber other)
        {
            return Equals(Message, other.Message);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is SendToOneSubscriber && Equals((SendToOneSubscriber)obj);
        }

        public override int GetHashCode()
        {
            return (Message != null ? Message.GetHashCode() : 0);
        }

        public override string ToString()
        {
            return $"SendToOneSubscriber<Message:{Message}>";
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
    internal sealed class NoMoreSubscribers : IChildActorTerminationProtocol
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static NoMoreSubscribers Instance { get; } = new NoMoreSubscribers();
        private NoMoreSubscribers() {}
    }

    /// <summary>
    /// Sent from parent to child actor to signalize that messages are being buffered. When received by child actor
    /// if no <see cref="Subscribe"/> message has been received after sending <see cref="NoMoreSubscribers"/> message child actor will stop itself.
    /// </summary>
    internal sealed class TerminateRequest : IChildActorTerminationProtocol
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static TerminateRequest Instance { get; } = new TerminateRequest();
        private TerminateRequest() {}
    }

    /// <summary>
    /// Sent from child to parent actor as response to <see cref="TerminateRequest"/> in case <see cref="Subscribe"/> message arrived
    /// after sending <see cref="NoMoreSubscribers"/> but before receiving <see cref="TerminateRequest"/>.
    /// When received by the parent buffered messages will be forwarded to child actor for processing.
    /// </summary>
    internal sealed class NewSubscriberArrived : IChildActorTerminationProtocol
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static NewSubscriberArrived Instance { get; } = new NewSubscriberArrived();
        private NewSubscriberArrived() { }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class MediatorRouterEnvelope : RouterEnvelope
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        public MediatorRouterEnvelope(object message) : base(message) { }
    }
}
