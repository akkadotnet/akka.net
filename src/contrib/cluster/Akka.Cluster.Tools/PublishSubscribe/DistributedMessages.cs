//-----------------------------------------------------------------------
// <copyright file="DistributedMessages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using System.Collections.Immutable;
using System.Linq;

namespace Akka.Cluster.Tools.PublishSubscribe
{
    /// <summary>
    /// Registers an actor with the distributed publish-subscribe mediator under its path.
    /// </summary>
    [Serializable]
    public sealed class Put : IEquatable<Put>
    {
        /// <summary>
        /// Actor to register with the mediator.
        /// </summary>
        public IActorRef Ref { get; }

        /// <summary>
        /// Creates a registration message for an actor.
        /// </summary>
        /// <param name="ref">Actor reference to register.</param>
        public Put(IActorRef @ref)
        {
            Ref = @ref;
        }

            
        public bool Equals(Put other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return Equals(Ref, other.Ref);
        }

       
        public override bool Equals(object obj)
        {
            return Equals(obj as Put);
        }

       
        public override int GetHashCode()
        {
            return (Ref != null ? Ref.GetHashCode() : 0);
        }

       
        public override string ToString()
        {
            return $"Put<ref:{Ref}>";
        }
    }

    /// <summary>
    /// Removes a path previously registered with the distributed publish-subscribe mediator.
    /// </summary>
    [Serializable]
    public sealed class Remove : IEquatable<Remove>
    {
        /// <summary>
        /// Actor path to remove from the mediator's registry.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Creates a removal message for a registered actor path.
        /// </summary>
        /// <param name="path">Path of the actor registration to remove.</param>
        public Remove(string path)
        {
            Path = path;
        }

       
        public bool Equals(Remove other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return Equals(Path, other.Path);
        }

       
        public override bool Equals(object obj)
        {
            return Equals(obj as Remove);
        }

       
        public override int GetHashCode()
        {
            return (Path != null ? Path.GetHashCode() : 0);
        }

       
        public override string ToString()
        {
            return $"Remove<path:{Path}>";
        }
    }

    /// <summary>
    /// Subscribes an actor to a topic, optionally as a member of a subscriber group.
    /// </summary>
    [Serializable]
    public sealed class Subscribe : IEquatable<Subscribe>
    {
        /// <summary>
        /// Topic to which the actor subscribes.
        /// </summary>
        public string Topic { get; }

        /// <summary>
        /// Optional group name used when a publication is configured to deliver to one subscriber per group.
        /// </summary>
        public string Group { get; }

        /// <summary>
        /// Actor that receives messages published to the topic.
        /// </summary>
        public IActorRef Ref { get; }

        /// <summary>
        /// Creates a subscription request for an actor and topic.
        /// </summary>
        /// <param name="topic">Topic to subscribe to; it must not be null or empty.</param>
        /// <param name="ref">Actor reference to subscribe.</param>
        /// <param name="group">Optional group name used by publications configured to deliver to one subscriber per group.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="topic"/> is undefined.
        /// </exception>
        public Subscribe(string topic, IActorRef @ref, string @group = null)
        {
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("topic must be defined");

            Topic = topic;
            Group = @group;
            Ref = @ref;
        }

       
        public bool Equals(Subscribe other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return Equals(Topic, other.Topic) &&
                   Equals(Group, other.Group) &&
                   Equals(Ref, other.Ref);
        }

       
        public override bool Equals(object obj)
        {
            return Equals(obj as Subscribe);
        }

       
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Topic != null ? Topic.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Group != null ? Group.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Ref != null ? Ref.GetHashCode() : 0);
                return hashCode;
            }
        }

       
        public override string ToString()
        {
            return $"Subscribe<topic:{Topic}, group:{Group}, ref:{Ref}>";
        }
    }

    /// <summary>
    /// Removes an actor's subscription to a topic and optional subscriber group.
    /// </summary>
    [Serializable]
    public sealed class Unsubscribe : IEquatable<Unsubscribe>
    {
        /// <summary>
        /// Topic from which the actor unsubscribes.
        /// </summary>
        public string Topic { get; }

        /// <summary>
        /// Optional subscriber group from which the actor unsubscribes.
        /// </summary>
        public string Group { get; }

        /// <summary>
        /// Actor whose subscription is removed.
        /// </summary>
        public IActorRef Ref { get; }

        /// <summary>
        /// Creates an unsubscription request for an actor and topic.
        /// </summary>
        /// <param name="topic">Topic to unsubscribe from; it must not be null or empty.</param>
        /// <param name="ref">Actor reference to unsubscribe.</param>
        /// <param name="group">Optional subscriber group from which to remove the actor.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="topic"/> is undefined.
        /// </exception>
        public Unsubscribe(string topic, IActorRef @ref, string @group = null)
        {
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("topic must be defined");

            Topic = topic;
            Group = @group;
            Ref = @ref;
        }

       
        public bool Equals(Unsubscribe other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return Equals(Topic, other.Topic) &&
                   Equals(Group, other.Group) &&
                   Equals(Ref, other.Ref);
        }

       
        public override bool Equals(object obj)
        {
            return Equals(obj as Unsubscribe);
        }

       
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Topic != null ? Topic.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Group != null ? Group.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Ref != null ? Ref.GetHashCode() : 0);
                return hashCode;
            }
        }

       
        public override string ToString()
        {
            return $"Unsubscribe<topic:{Topic}, group:{Group}, ref:{Ref}>";
        }
    }

    /// <summary>
    /// Acknowledges processing of a subscription request.
    /// </summary>
    [Serializable]
    public sealed class SubscribeAck : IEquatable<SubscribeAck>, IDeadLetterSuppression
    {
        /// <summary>
        /// Subscription request acknowledged by the mediator.
        /// </summary>
        public Subscribe Subscribe { get; }

        /// <summary>
        /// Creates an acknowledgement for a subscription request.
        /// </summary>
        /// <param name="subscribe">Subscription request that was processed.</param>
        public SubscribeAck(Subscribe subscribe)
        {
            Subscribe = subscribe;
        }

       
        public bool Equals(SubscribeAck other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return Equals(Subscribe, other.Subscribe);
        }

       
        public override bool Equals(object obj)
        {
            return Equals(obj as SubscribeAck);
        }

       
        public override int GetHashCode()
        {
            return (Subscribe != null ? Subscribe.GetHashCode() : 0);
        }

       
        public override string ToString()
        {
            return $"SubscribeAck<{Subscribe}>";
        }
    }

    /// <summary>
    /// Acknowledges processing of an unsubscription request.
    /// </summary>
    [Serializable]
    public sealed class UnsubscribeAck : IEquatable<UnsubscribeAck>
    {
        /// <summary>
        /// Unsubscription request acknowledged by the mediator.
        /// </summary>
        public Unsubscribe Unsubscribe { get; }

        /// <summary>
        /// Publishes a message to subscribers of a topic.
        /// </summary>
        public UnsubscribeAck(Unsubscribe unsubscribe)
        {
            Unsubscribe = unsubscribe;
        }

       
        public bool Equals(UnsubscribeAck other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return Equals(Unsubscribe, other.Unsubscribe);
        }

       
        public override bool Equals(object obj)
        {
            return Equals(obj as UnsubscribeAck);
        }

       
        public override int GetHashCode()
        {
            return (Unsubscribe != null ? Unsubscribe.GetHashCode() : 0);
        }

       
        public override string ToString()
        {
            return $"UnsubscribeAck<{Unsubscribe}>";
        }
    }

    /// <summary>
    /// Publishes a message to subscribers of a topic.
    /// </summary>
    [Serializable]
    public sealed class Publish : IDistributedPubSubMessage, IEquatable<Publish>, IWrappedMessage
    {
        /// <summary>
        /// Topic to which the message is published.
        /// </summary>
        public string Topic { get; }
        /// <summary>
        /// Message delivered to topic subscribers.
        /// </summary>
        public object Message { get; }
        /// <summary>
        /// Whether to send the publication to one subscriber in each group and every ungrouped subscriber.
        /// </summary>
        public bool SendOneMessageToEachGroup { get; }

        /// <summary>
        /// Creates a message publication request.
        /// </summary>
        /// <param name="topic">Topic whose subscribers receive the message.</param>
        /// <param name="message">Message to publish.</param>
        /// <param name="sendOneMessageToEachGroup">If <see langword="true"/>, deliver to one subscriber in each group and every ungrouped subscriber; otherwise deliver to every subscriber.</param>
        public Publish(string topic, object message, bool sendOneMessageToEachGroup = false)
        {
            Topic = topic;
            Message = message;
            SendOneMessageToEachGroup = sendOneMessageToEachGroup;
        }

       
        public bool Equals(Publish other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return Equals(Topic, other.Topic) &&
                   Equals(SendOneMessageToEachGroup, other.SendOneMessageToEachGroup) &&
                   Equals(Message, other.Message);
        }

       
        public override bool Equals(object obj)
        {
            return Equals(obj as Publish);
        }

       
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Topic != null ? Topic.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Message != null ? Message.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ SendOneMessageToEachGroup.GetHashCode();
                return hashCode;
            }
        }

       
        public override string ToString()
        {
            return $"Publish<topic:{Topic}, sendOneToEachGroup:{SendOneMessageToEachGroup}, message:{Message}>";
        }
    }
    
    public sealed record PublishWithAck : IDistributedPubSubMessage, IWrappedMessage
    {
        public PublishWithAck(string topic, object message, TimeSpan timeout, bool sendOneMessageToEachGroup = false)
        {
            if(timeout.Ticks <= 0)
                throw new ArgumentException("Timeout must be greater than zero", nameof(timeout));
            
            Topic = topic;
            Message = message;
            Timeout = timeout;
            SendOneMessageToEachGroup = sendOneMessageToEachGroup;
        }
        
        public string Topic { get; }
        public object Message { get; }
        public TimeSpan Timeout { get; }
        public bool SendOneMessageToEachGroup { get; }
    }

    public enum PublishFailReason
    {
        Timeout,
        MediatorShuttingDown
    }
    
    public interface IPublishResponse;
    
    public sealed record PublishFailed(PublishWithAck Message, PublishFailReason Reason): IPublishResponse, IDeadLetterSuppression;
    
    public sealed record PublishSucceeded(PublishWithAck Message): IPublishResponse, IDeadLetterSuppression;

    /// <summary>
    /// Sends a message to one actor registered at a matching path.
    /// </summary>
    [Serializable]
    public sealed class Send : IDistributedPubSubMessage, IEquatable<Send>, IWrappedMessage
    {
        /// <summary>
        /// Actor path used to select a registered recipient.
        /// </summary>
        public string Path { get; }
        /// <summary>
        /// Message forwarded to the selected recipient.
        /// </summary>
        public object Message { get; }
        /// <summary>
        /// Whether to prefer a matching recipient in the same local actor system as the mediator.
        /// </summary>
        public bool LocalAffinity { get; }

        /// <summary>
        /// Creates a request to send a message to one matching registered actor.
        /// </summary>
        /// <param name="path">Actor path used to select matching registered actors.</param>
        /// <param name="message">Message to deliver.</param>
        /// <param name="localAffinity">If <see langword="true"/>, prefer a recipient local to the mediator when one matches.</param>
        public Send(string path, object message, bool localAffinity = false)
        {
            Path = path;
            Message = message;
            LocalAffinity = localAffinity;
        }

       
        public bool Equals(Send other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return Equals(Path, other.Path) &&
                   Equals(LocalAffinity, other.LocalAffinity) &&
                   Equals(Message, other.Message);
        }

       
        public override bool Equals(object obj)
        {
            return Equals(obj as Send);
        }

       
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Path != null ? Path.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Message != null ? Message.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ LocalAffinity.GetHashCode();
                return hashCode;
            }
        }

       
        public override string ToString()
        {
            return $"Send<path:{Path}, localAffinity:{LocalAffinity}, message:{Message}>";
        }
    }

    /// <summary>
    /// Sends a message to every actor registered at a matching path.
    /// </summary>
    [Serializable]
    public sealed class SendToAll : IDistributedPubSubMessage, IEquatable<SendToAll>, IWrappedMessage
    {
        /// <summary>
        /// Actor path used to select registered recipients.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Message forwarded to each matching recipient.
        /// </summary>
        public object Message { get; }

        /// <summary>
        /// Whether to omit matching recipients on the mediator's local cluster node.
        /// </summary>
        public bool ExcludeSelf { get; }

        /// <summary>
        /// Creates a request to send a message to all matching registered actors.
        /// </summary>
        /// <param name="path">Actor path used to select matching registered actors.</param>
        /// <param name="message">Message to deliver.</param>
        /// <param name="excludeSelf">If <see langword="true"/>, exclude matching recipients on the mediator's local cluster node.</param>
        public SendToAll(string path, object message, bool excludeSelf = false)
        {
            Path = path;
            Message = message;
            ExcludeSelf = excludeSelf;
        }

       
        public bool Equals(SendToAll other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return Equals(ExcludeSelf, other.ExcludeSelf) &&
                   Equals(Path, other.Path) &&
                   Equals(Message, other.Message);
        }

       
        public override bool Equals(object obj)
        {
            return Equals(obj as SendToAll);
        }

       
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Path != null ? Path.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Message != null ? Message.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ ExcludeSelf.GetHashCode();
                return hashCode;
            }
        }

       
        public override string ToString()
        {
            return $"SendToAll<path:{Path}, excludeSelf:{ExcludeSelf}, message:{Message}>";
        }
    }

    /// <summary>
    /// Requests the set of topic names currently known to the mediator.
    /// </summary>
    [Serializable]
    public sealed class GetTopics
    {
        /// <summary>
        /// Singleton instance of the topic-list request.
        /// </summary>
        public static GetTopics Instance { get; } = new();
        private GetTopics() { }
    }

    /// <summary>
    /// Reply containing the topic names currently known to the mediator.
    /// </summary>
    [Serializable]
    public sealed class CurrentTopics : IEquatable<CurrentTopics>
    {
        /// <summary>
        /// Set of topic names known to the mediator.
        /// </summary>
        public IImmutableSet<string> Topics { get; }

        /// <summary>
        /// Creates a reply containing the known topic names.
        /// </summary>
        /// <param name="topics">Topic names to return; a null value is treated as an empty set.</param>
        public CurrentTopics(IImmutableSet<string> topics)
        {
            Topics = topics ?? ImmutableHashSet<string>.Empty;
        }

       
        public bool Equals(CurrentTopics other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;

            return Topics.SequenceEqual(other.Topics);
        }

       
        public override bool Equals(object obj)
        {
            return Equals(obj as CurrentTopics);
        }

       
        public override int GetHashCode()
        {
            return (Topics != null ? Topics.GetHashCode() : 0);
        }

       
        public override string ToString()
        {
            return $"CurrentTopics<{string.Join(",", Topics)}>";
        }
    }
}
