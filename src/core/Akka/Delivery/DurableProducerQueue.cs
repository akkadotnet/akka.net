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
    public interface IDurableProducerQueueCommand<T>
    {
    }

    /// <summary>
    ///     Request used at startup to retrieve the unconfirmed messages and current sequence number.
    /// </summary>
    public sealed class LoadState<T> : IDurableProducerQueueCommand<T>
    {
        public LoadState(IActorRef replyTo)
        {
            ReplyTo = replyTo;
        }

        public IActorRef ReplyTo { get; }
    }

    /// <summary>
    ///     Store the fact that a message is to be sent. Replies with <see cref="StoreMessageSentAck" />
    ///     when the message has been successfully stored.
    /// </summary>
    /// <remarks>
    ///     This command may be retried and the implementation should be idempotent.
    /// </remarks>
    public sealed class StoreMessageSent<T> : IDurableProducerQueueCommand<T>
    {
        public StoreMessageSent(MessageSent<T> messageSent, IActorRef replyTo)
        {
            MessageSent = messageSent;
            ReplyTo = replyTo;
        }

        public IActorRef ReplyTo { get; }

        public MessageSent<T> MessageSent { get; }
    }

    public sealed class StoreMessageSentAck
    {
        public StoreMessageSentAck(long storedSeqNo)
        {
            StoredSeqNo = storedSeqNo;
        }

        public long StoredSeqNo { get; }
    }

    /// <summary>
    ///     Store the fact that a message has been delivered and processed.
    /// </summary>
    /// <remarks>
    ///     This command may be retried and the implementation should be idempotent.
    /// </remarks>
    public sealed class StoreMessageConfirmed<T> : IDurableProducerQueueCommand<T>
    {
        public StoreMessageConfirmed(long seqNr, string confirmationQualifier, long timestamp)
        {
            ConfirmationQualifier = confirmationQualifier;
            SeqNr = seqNr;
            Timestamp = timestamp;
        }

        public string ConfirmationQualifier { get; }

        public long SeqNr { get; }

        public long Timestamp { get; }
    }

    /// <summary>
    ///     Durable producer queue state
    /// </summary>
    public record struct State<T> : IDeliverySerializable, IEquatable<State<T>>
    {
        public static State<T> Empty { get; } = new(1, 0, ImmutableDictionary<string, (long, long)>.Empty,
            ImmutableList<MessageSent<T>>.Empty);

        public State(long currentSeqNr, long highestConfirmedSeqNr,
            ImmutableDictionary<string, (long, long)> confirmedSeqNr, ImmutableList<MessageSent<T>> unconfirmed)
        {
            CurrentSeqNr = currentSeqNr;
            HighestConfirmedSeqNr = highestConfirmedSeqNr;
            ConfirmedSeqNr = confirmedSeqNr;
            Unconfirmed = unconfirmed;
        }

        public long CurrentSeqNr { get; init; }

        public long HighestConfirmedSeqNr { get; init; }

        public ImmutableDictionary<string, (long, long)> ConfirmedSeqNr { get; init; }

        public ImmutableList<MessageSent<T>> Unconfirmed { get; init; }

        public State<T> AddMessageSent(MessageSent<T> messageSent)
        {
            return new State<T>(messageSent.SeqNr + 1, HighestConfirmedSeqNr,
                ConfirmedSeqNr, Unconfirmed.Add(messageSent));
        }

        public State<T> AddConfirmed(long seqNr, string qualifier, long timestamp)
        {
            var newUnconfirmed = Unconfirmed.Where(c => !(c.SeqNr <= seqNr && c.ConfirmationQualifier == qualifier))
                .ToImmutableList();

            return new State<T>(CurrentSeqNr, Math.Max(HighestConfirmedSeqNr, seqNr),
                ConfirmedSeqNr.SetItem(qualifier, (seqNr, timestamp)), newUnconfirmed);
        }

        public State<T> CleanUp(ISet<string> confirmationQualifiers)
        {
            return new State<T>(CurrentSeqNr, HighestConfirmedSeqNr, ConfirmedSeqNr.RemoveRange(confirmationQualifiers),
                Unconfirmed);
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

            return new State<T>(newCurrentSeqNr, HighestConfirmedSeqNr, ConfirmedSeqNr, newUnconfirmed.ToImmutable());
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
    }

    /// <summary>
    ///     Internal events that are persisted by the durable producer queue.
    /// </summary>
    internal interface IDurableProducerQueueEvent : IDeliverySerializable
    {
    }

    /// <summary>
    ///     The fact that a message has been sent.
    /// </summary>
    public sealed class MessageSent<T> : IDurableProducerQueueEvent, IEquatable<MessageSent<T>>
    {
        public MessageSent(long seqNr, MessageOrChunk<T> message, bool ack, string confirmationQualifier,
            long timestamp)
        {
            SeqNr = seqNr;
            Message = message;
            Ack = ack;
            ConfirmationQualifier = confirmationQualifier;
            Timestamp = timestamp;
        }

        public long SeqNr { get; }

        public MessageOrChunk<T> Message { get; }

        public bool Ack { get; }

        public string ConfirmationQualifier { get; }

        public long Timestamp { get; }

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
            return new MessageSent<T>(SeqNr, Message, Ack, qualifier, Timestamp);
        }

        public MessageSent<T> WithTimestamp(long timestamp)
        {
            return new MessageSent<T>(SeqNr, Message, Ack, ConfirmationQualifier, timestamp);
        }

        public override bool Equals(object? obj)
        {
            return ReferenceEquals(this, obj) || obj is MessageSent<T> other && Equals(other);
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

        public void Deconstruct(out long seqNo, out MessageOrChunk<T> message, out bool ack,
            out string qualifier, out long timestamp)
        {
            seqNo = SeqNr;
            message = Message;
            ack = Ack;
            qualifier = ConfirmationQualifier;
            timestamp = Timestamp;
        }
    }

    /// <summary>
    ///     INTERNAL API
    ///     The fact that a message has been confirmed to be delivered and processed.
    /// </summary>
    internal sealed class Confirmed : IDurableProducerQueueEvent
    {
        public Confirmed(long seqNo, string qualifier, long timestamp)
        {
            SeqNo = seqNo;
            Qualifier = qualifier;
            Timestamp = timestamp;
        }

        public long SeqNo { get; }

        public string Qualifier { get; }

        public long Timestamp { get; }

        public override string ToString()
        {
            return $"Confirmed({SeqNo}, {Qualifier}, {Timestamp})";
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