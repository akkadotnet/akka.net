// -----------------------------------------------------------------------
//  <copyright file="DurableProducerQueue.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Delivery.Internal;

namespace Akka.Delivery;

public static class DurableProducerQueue
{
    public const string NoQualifier = "";

    /// <summary>
    ///     Marker interface for all commands handled by the durable producer queue.
    /// </summary>
    /// <typeparam name="T">The same type of messages that are handled by the ProducerController{T}</typeparam>
    public interface IDurableProducerQueueCommand
    {
    }

    /// <summary>
    ///     Request used at startup to retrieve the unconfirmed messages and current sequence number.
    /// </summary>
    public sealed record LoadState(IActorRef ReplyTo) : IDurableProducerQueueCommand;

    /// <summary>
    ///     Store the fact that a message is to be sent. Replies with <see cref="StoreMessageSentAck" />
    ///     when the message has been successfully stored.
    /// </summary>
    /// <remarks>
    ///     This command may be retried and the implementation should be idempotent.
    /// </remarks>
    public sealed record StoreMessageSent<T>(MessageSent<T> MessageSent, IActorRef ReplyTo) : IDurableProducerQueueCommand;

    public sealed record StoreMessageSentAck(long StoredSeqNo);

    /// <summary>
    ///     Store the fact that a message has been delivered and processed.
    /// </summary>
    /// <remarks>
    ///     This command may be retried and the implementation should be idempotent.
    /// </remarks>
    public sealed record StoreMessageConfirmed(long SeqNr, string ConfirmationQualifier, long Timestamp) : IDurableProducerQueueCommand;

    /// <summary>
    /// INTERNAL API - used for serialization
    /// </summary>
    internal interface IState
    {
        Type MessageType { get; }
    }

    /// <summary>
    ///     Durable producer queue state
    /// </summary>
    public readonly record struct State<T>(long CurrentSeqNr, long HighestConfirmedSeqNr,
        ImmutableDictionary<string, (long, long)> ConfirmedSeqNr, ImmutableList<MessageSent<T>> Unconfirmed) : IDeliverySerializable, IState
    {
        public static State<T> Empty { get; } = new(1, 0, ImmutableDictionary<string, (long, long)>.Empty,
            ImmutableList<MessageSent<T>>.Empty);

        public State<T> AddMessageSent(MessageSent<T> messageSent)
        {
            return this with { CurrentSeqNr = messageSent.SeqNr + 1, Unconfirmed = Unconfirmed.Add(messageSent) };
        }

        public State<T> AddConfirmed(long seqNr, string qualifier, long timestamp)
        {
            var newUnconfirmed = Unconfirmed.Where(c => !(c.SeqNr <= seqNr && c.ConfirmationQualifier == qualifier))
                .ToImmutableList();

            return this with
            {
                HighestConfirmedSeqNr = Math.Max(HighestConfirmedSeqNr, seqNr),
                ConfirmedSeqNr = ConfirmedSeqNr.SetItem(qualifier, (seqNr, timestamp)),
                Unconfirmed = newUnconfirmed
            };
        }

        public State<T> CleanUp(ISet<string> confirmationQualifiers)
        {
            return this with { ConfirmedSeqNr = ConfirmedSeqNr.RemoveRange(confirmationQualifiers) };
        }

        /// <summary>
        ///     If not all chunked messages were stored before crash, those partial messages should not be resent.
        /// </summary>
        public State<T> CleanUpPartialChunkedMessages()
        {
            if (Unconfirmed.IsEmpty || Unconfirmed.All(u => u.IsFirstChunk && u.IsLastChunk))
                return this;

            var tmp = ImmutableList.CreateBuilder<MessageSent<T>>();
            var newUnconfirmed = ImmutableList.CreateBuilder<MessageSent<T>>();
            var newCurrentSeqNr = HighestConfirmedSeqNr + 1;
            foreach (var u in Unconfirmed)
                if (u.IsFirstChunk && u.IsLastChunk)
                {
                    tmp.Clear();
                    newUnconfirmed.Add(u);
                    newCurrentSeqNr = u.SeqNr + 1;
                }
                else if (u is { IsFirstChunk: true, IsLastChunk: false })
                {
                    tmp.Clear();
                    tmp.Add(u);
                }
                else if (!u.IsLastChunk)
                {
                    tmp.Add(u);
                }
                else if (u.IsLastChunk)
                {
                    newUnconfirmed.AddRange(tmp.ToImmutable());
                    newUnconfirmed.Add(u);
                    newCurrentSeqNr = u.SeqNr + 1;
                    tmp.Clear();
                }

            return this with { CurrentSeqNr = newCurrentSeqNr, Unconfirmed = newUnconfirmed.ToImmutable() };
        }

        public bool Equals(State<T> other)
        {
            return CurrentSeqNr == other.CurrentSeqNr && HighestConfirmedSeqNr == other.HighestConfirmedSeqNr &&
                   ConfirmedSeqNr.SequenceEqual(other.ConfirmedSeqNr) && Unconfirmed.SequenceEqual(other.Unconfirmed);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = CurrentSeqNr.GetHashCode();
                hashCode = (hashCode * 397) ^ HighestConfirmedSeqNr.GetHashCode();
                hashCode = (hashCode * 397) ^ ConfirmedSeqNr.GetHashCode();
                hashCode = (hashCode * 397) ^ Unconfirmed.GetHashCode();
                return hashCode;
            }
        }

        Type IState.MessageType => typeof(T);
    }

    /// <summary>
    ///     Internal events that are persisted by the durable producer queue.
    /// </summary>
    internal interface IDurableProducerQueueEvent : IDeliverySerializable
    {
    }


    /// <summary>
    /// INTERNAL API - used for serialization
    /// </summary>
    internal interface IMessageSent
    {
        Type MessageType { get; }
    }

    /// <summary>
    ///     The fact that a message has been sent.
    /// </summary>
    public sealed record MessageSent<T>(long SeqNr, MessageOrChunk<T> Message, bool Ack, string ConfirmationQualifier,
        long Timestamp) : IDurableProducerQueueEvent, IMessageSent
    {
        internal bool IsFirstChunk => Message.Chunk is { FirstChunk: true } or null;

        internal bool IsLastChunk => Message.Chunk is { LastChunk: true } or null;

        public bool Equals(MessageSent<T>? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return SeqNr == other.SeqNr && Message.Equals(other.Message) && Ack == other.Ack &&
                   ConfirmationQualifier == other.ConfirmationQualifier && Timestamp == other.Timestamp;
        }

        public MessageSent<T> WithQualifier(string qualifier)
        {
            return this with { ConfirmationQualifier = qualifier };
        }

        public MessageSent<T> WithTimestamp(long timestamp)
        {
            return this with { Timestamp = timestamp };
        }
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = SeqNr.GetHashCode();
                hashCode = (hashCode * 397) ^ Message.GetHashCode();
                hashCode = (hashCode * 397) ^ Ack.GetHashCode();
                hashCode = (hashCode * 397) ^ ConfirmationQualifier.GetHashCode();
                hashCode = (hashCode * 397) ^ Timestamp.GetHashCode();
                return hashCode;
            }
        }

        public override string ToString()
        {
            return $"MessageSent({SeqNr}, {Message}, {Ack}, {ConfirmationQualifier}, {Timestamp})";
        }

        public static MessageSent<T> FromChunked(long seqNo, ChunkedMessage chunkedMessage, bool ack,
            string confirmationQualifier, long timestamp)
        {
            return new MessageSent<T>(seqNo, chunkedMessage, ack, confirmationQualifier, timestamp);
        }

        public static MessageSent<T> FromMessageOrChunked(long seqNo, MessageOrChunk<T> messageOrChunk, bool ack,
            string confirmationQualifier, long timestamp)
        {
            return new MessageSent<T>(seqNo, messageOrChunk, ack, confirmationQualifier, timestamp);
        }

        Type IMessageSent.MessageType => typeof(T);
    }

    /// <summary>
    ///     INTERNAL API
    ///     The fact that a message has been confirmed to be delivered and processed.
    /// </summary>
    internal sealed record Confirmed(long SeqNr, string Qualifier, long Timestamp) : IDurableProducerQueueEvent
    {
        public override string ToString()
        {
            return $"Confirmed({SeqNr}, {Qualifier}, {Timestamp})";
        }
    }

    /// <summary>
    ///     INTERNAL API
    ///     Remove entries related to the ConfirmationQualifiers that haven't been used in a while.
    /// </summary>
    internal sealed class Cleanup : IDurableProducerQueueEvent
    {
        public Cleanup(ISet<string> confirmationQualifiers)
        {
            ConfirmationQualifiers = confirmationQualifiers;
        }

        public ISet<string> ConfirmationQualifiers { get; }

        public override string ToString()
        {
            return $"Cleanup({string.Join(", ", ConfirmationQualifiers)})";
        }
    }
}