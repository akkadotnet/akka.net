//-----------------------------------------------------------------------
// <copyright file="TopicMessages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Annotations;
using Akka.Event;
using Akka.Remote;
using Akka.Routing;

namespace Akka.Cluster.Tools.PublishSubscribe.Internal
{
    /// <summary>
    /// Periodic message that asks a topic or group actor to check whether it should be pruned.
    /// </summary>
    [Serializable]
    internal sealed class Prune
    {
        /// <summary>
        /// Singleton instance of the prune tick.
        /// </summary>
        public static Prune Instance { get; } = new();
        private Prune() { }
    }

    // Only for testing purposes, to poll/await replication
    /// <summary>
    /// Test-only request that returns the number of subscribers held by an actor.
    /// </summary>
    internal sealed class Count
    {
        /// <summary>
        /// Singleton instance of the subscriber-count request.
        /// </summary>
        public static Count Instance { get; } = new();
        private Count() { }
    }

    /// <summary>
    /// Get all subscribers for a given topic.
    /// </summary>
    [ApiMayChange]
    public sealed class CountSubscribers
    {
        public string Topic { get; }

        public CountSubscribers(string topic)
        {
            Topic = topic;
        }
    }

    /// <summary>
    /// Versioned set of publish-subscribe registrations owned by one cluster member.
    /// </summary>
    [Serializable]
    internal class Bucket : IEquatable<Bucket>
    {
        /// <summary>
        /// Address of the member that owns this registry bucket.
        /// </summary>
        public Address Owner { get; }

        /// <summary>
        /// Version of the owner's registry represented by this bucket.
        /// </summary>
        public long Version { get; }

        /// <summary>
        /// Registered actor paths and their versioned actor references for this owner.
        /// </summary>
        public IImmutableDictionary<string, ValueHolder> Content { get; }

        /// <summary>
        /// Creates an empty registry bucket owned by the specified member.
        /// </summary>
        /// <param name="owner">Address of the member that owns the bucket.</param>
        public Bucket(Address owner) : this(owner, 0L, ImmutableDictionary<string, ValueHolder>.Empty)
        {
        }

        /// <summary>
        /// Creates a registry bucket with the supplied version and contents.
        /// </summary>
        /// <param name="owner">Address of the member that owns the bucket.</param>
        /// <param name="version">Version of the owner's registry.</param>
        /// <param name="content">Actor path registrations and their versioned values.</param>
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
    /// Versioned registration of an actor reference in an owner's registry bucket.
    /// </summary>
    [Serializable]
    internal sealed class ValueHolder : IEquatable<ValueHolder>
    {
        /// <summary>
        /// Version assigned to this registration.
        /// </summary>
        public long Version { get; }

        /// <summary>
        /// Actor reference registered at the corresponding path.
        /// </summary>
        public IActorRef Ref { get; }

        [NonSerialized]
        private Routee _routee;

        /// <summary>
        /// Creates a versioned registration value.
        /// </summary>
        /// <param name="version">Version assigned to this registration.</param>
        /// <param name="ref">Actor reference registered at the path.</param>
        public ValueHolder(long version, IActorRef @ref)
        {
            Version = version;
            Ref = @ref;
        }

        /// <summary>
        /// Lazily created routee for the registered actor, or <see langword="null"/> when no actor reference is available.
        /// </summary>
        public Routee Routee { get { return _routee ??= Ref != null ? new ActorRefRoutee(Ref) : null; } }

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
    /// Version summary exchanged between mediators to determine which registry buckets need synchronization.
    /// </summary>
    [Serializable]
    internal sealed class Status : IDistributedPubSubMessage, IDeadLetterSuppression
    {
        /// <summary>
        /// Creates a status message with owner versions and its request/reply flag.
        /// </summary>
        /// <param name="versions">Latest registry version observed for each owner.</param>
        /// <param name="isReplyToStatus">Whether this status is a reply to another mediator's status message.</param>
        public Status(IImmutableDictionary<Address, long> versions, bool isReplyToStatus)
        {
            Versions = versions ?? ImmutableDictionary<Address, long>.Empty;
            IsReplyToStatus = isReplyToStatus;
        }

        /// <summary>
        /// Latest registry version observed for each owner; a null value is treated as an empty map.
        /// </summary>
        public IImmutableDictionary<Address, long> Versions { get; }

        /// <summary>
        /// Indicates whether this status replies to a status request.
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
    /// Registry bucket updates replicated between distributed publish-subscribe mediators.
    /// </summary>
    [Serializable]
    internal sealed class Delta : IDistributedPubSubMessage, IEquatable<Delta>, IDeadLetterSuppression
    {
        /// <summary>
        /// Buckets carrying the registry updates in this delta.
        /// </summary>
        public IImmutableList<Bucket> Buckets { get; }

        /// <summary>
        /// Creates a delta with registry updates.
        /// </summary>
        /// <param name="buckets">Buckets to include; a null value is treated as an empty list.</param>
        public Delta(IImmutableList<Bucket> buckets)
        {
            Buckets = buckets ?? ImmutableList<Bucket>.Empty;
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
    /// Test-only message that returns the number of delta updates sent during replication.
    /// </summary>
    [Serializable]
    internal sealed class DeltaCount
    {
        /// <summary>
        /// Singleton instance of the delta-count request.
        /// </summary>
        public static readonly DeltaCount Instance = new();

        private DeltaCount() { }
    }

    /// <summary>
    /// Periodic message that triggers registry gossip between mediators.
    /// </summary>
    [Serializable]
    internal sealed class GossipTick: IDeadLetterSuppression
    {
        public static GossipTick Instance { get; } = new();
        private GossipTick() { }
    }

    /// <summary>
    /// Internal event signalling that a new subscriber has been added to the registry
    /// either locally using <see cref="Put"/>, <see cref="Subscribe"/>, or from a <see cref="Delta"/>.
    /// </summary>
    internal sealed record NewBucketKeysAdded(IReadOnlyList<string> Topics): IDeadLetterSuppression;
    
    /// <summary>
    /// Container for buffered <see cref="Publish"/> or <see cref="Send"/> messages
    /// </summary>
    /// <param name="Message">The original message being buffered</param>
    /// <param name="Deadline">The deadline where this buffered message should be timed out</param>
    /// <param name="Sender">The original sender of the message</param>
    internal readonly record struct BufferedMessage(IWrappedMessage Message, Deadline Deadline, IActorRef Sender);

    internal sealed class PruneBufferTick: IDeadLetterSuppression
    {
        public static PruneBufferTick Instance { get; } = new();
        private PruneBufferTick() { }
    }
    
    /// <summary>
    /// Registers a topic or group child actor with its mediator parent.
    /// </summary>
    [Serializable]
    internal sealed class RegisterTopic
    {
        /// <summary>
        /// Topic or group actor to register with the parent mediator.
        /// </summary>
        public IActorRef TopicRef { get; }

        /// <summary>
        /// Creates a registration message for a topic or group actor.
        /// </summary>
        /// <param name="topicRef">Topic or group actor reference to register.</param>
        public RegisterTopic(IActorRef topicRef)
        {
            TopicRef = topicRef;
        }
    }

    /// <summary>
    /// Child-to-parent message carrying a subscription acknowledgement and its original requester.
    /// </summary>
    [Serializable]
    internal sealed class Subscribed
    {
        /// <summary>
        /// Acknowledgement produced for the subscription.
        /// </summary>
        public SubscribeAck Ack { get; }

        /// <summary>
        /// Original requester to which the mediator forwards the acknowledgement.
        /// </summary>
        public IActorRef Subscriber { get; }

        /// <summary>
        /// Creates a message reporting a processed subscription.
        /// </summary>
        /// <param name="ack">Acknowledgement for the subscription.</param>
        /// <param name="subscriber">Original requester that should receive the acknowledgement.</param>
        public Subscribed(SubscribeAck ack, IActorRef subscriber)
        {
            Ack = ack;
            Subscriber = subscriber;
        }
    }

    /// <summary>
    /// Child-to-parent message carrying an unsubscription acknowledgement and its original requester.
    /// </summary>
    [Serializable]
    internal sealed class Unsubscribed
    {
        /// <summary>
        /// Acknowledgement produced for the unsubscription.
        /// </summary>
        public UnsubscribeAck Ack { get; }

        /// <summary>
        /// Original requester to which the mediator forwards the acknowledgement.
        /// </summary>
        public IActorRef Subscriber { get; }

        /// <summary>
        /// Creates a message reporting a processed unsubscription.
        /// </summary>
        /// <param name="ack">Acknowledgement for the unsubscription.</param>
        /// <param name="subscriber">Original requester that should receive the acknowledgement.</param>
        public Unsubscribed(UnsubscribeAck ack, IActorRef subscriber)
        {
            Ack = ack;
            Subscriber = subscriber;
        }
    }

    /// <summary>
    /// Wraps a publication so a group actor routes it to one of its subscribers.
    /// </summary>
    [Serializable]
    internal sealed class SendToOneSubscriber
    {
        /// <summary>
        /// Message forwarded to the selected group subscriber.
        /// </summary>
        public object Message { get; }

        /// <summary>
        /// Creates an envelope for delivery to one subscriber in a group.
        /// </summary>
        /// <param name="message">Published message to forward.</param>
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
            return obj is SendToOneSubscriber subscriber && Equals(subscriber);
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
        /// Singleton signal that a child topic or group has no subscribers and no children.
        /// </summary>
        public static NoMoreSubscribers Instance { get; } = new();
        private NoMoreSubscribers() {}
    }

    /// <summary>
    /// Sent from parent to child actor to signalize that messages are being buffered. When received by child actor
    /// if no <see cref="Subscribe"/> message has been received after sending <see cref="NoMoreSubscribers"/> message child actor will stop itself.
    /// </summary>
    internal sealed class TerminateRequest : IChildActorTerminationProtocol
    {
        /// <summary>
        /// Singleton request from a parent asking a child to stop after a passivation signal.
        /// </summary>
        public static TerminateRequest Instance { get; } = new();
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
        /// Singleton response indicating that a subscriber arrived before a child terminated.
        /// </summary>
        public static NewSubscriberArrived Instance { get; } = new();
        private NewSubscriberArrived() { }
    }

    /// <summary>
    /// Envelope that prevents a router from unwrapping a user-supplied router envelope prematurely.
    /// </summary>
    [Serializable]
    internal sealed class MediatorRouterEnvelope : RouterEnvelope
    {
        /// <summary>
        /// Creates a mediator-specific wrapper for a user message.
        /// </summary>
        /// <param name="message">User message to wrap before router delivery.</param>
        public MediatorRouterEnvelope(object message) : base(message) { }
    }
}
