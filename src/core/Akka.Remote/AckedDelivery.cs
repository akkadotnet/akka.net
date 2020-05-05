//-----------------------------------------------------------------------
// <copyright file="AckedDelivery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Actor;

namespace Akka.Remote
{
    /// <summary>
    /// Implements a 64-bit sequence number with proper overflow ordering
    /// </summary>
    internal sealed class SeqNo : IComparable<SeqNo>, IEquatable<SeqNo>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="rawValue">TBD</param>
        public SeqNo(long rawValue)
        {
            RawValue = rawValue;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public long RawValue { get; private set; }

        /// <summary>
        /// Checks if this sequence number is an immediate successor of the provided one.
        /// </summary>
        /// <param name="that">The second sequence number that has to be exactly one less</param>
        /// <returns>true if this sequence number is the successor of the provided one</returns>
        public bool IsSuccessor(SeqNo that)
        {
            return RawValue - that.RawValue == 1;
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public SeqNo Inc()
        {
            long nextValue;
            unchecked
            {
                nextValue = RawValue + 1;
            }
            return new SeqNo(nextValue);
        }

        #region IComparable<SeqNo>

        /// <inheritdoc/>
        public int CompareTo(SeqNo other)
        {
            return CompareSeq(this, other);
        }

        #endregion

        #region Operators / Equality


        /// <summary>
        /// Compares two specified sequence numbers to see if the first one is less than the other one.
        /// </summary>
        /// <param name="left">The first sequence number used for comparison</param>
        /// <param name="right">The second sequence number used for comparison</param>
        /// <returns><c>true</c> if the first sequence number is less than the other one; otherwise <c>false</c></returns>
        public static bool operator <(SeqNo left, SeqNo right)
        {
            return left.CompareTo(right) < 0;
        }

        /// <summary>
        /// Compares two specified sequence numbers to see if the first one is less than or equal to the other one.
        /// </summary>
        /// <param name="left">The first sequence number used for comparison</param>
        /// <param name="right">The second sequence number used for comparison</param>
        /// <returns><c>true</c> if the first sequence number is less than or equal to the other one; otherwise <c>false</c></returns>
        public static bool operator <=(SeqNo left, SeqNo right)
        {
            return left.CompareTo(right) <= 0;
        }

        /// <inheritdoc/>
        public bool Equals(SeqNo other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return RawValue == other.RawValue;
        }

        /// <summary>
        /// Compares two specified sequence numbers for equality.
        /// </summary>
        /// <param name="left">The first sequence number used for comparison</param>
        /// <param name="right">The second sequence number used for comparison</param>
        /// <returns><c>true</c> if both sequence numbers are equal; otherwise <c>false</c></returns>
        public static bool operator ==(SeqNo left, SeqNo right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two specified sequence numbers for inequality.
        /// </summary>
        /// <param name="left">The first sequence number used for comparison</param>
        /// <param name="right">The second sequence number used for comparison</param>
        /// <returns><c>true</c> if both sequence numbers are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(SeqNo left, SeqNo right)
        {
            return !Equals(left, right);
        }

        /// <summary>
        /// Compares two specified sequence numbers to see if the first one is greater than the other one.
        /// </summary>
        /// <param name="left">The first sequence number used for comparison</param>
        /// <param name="right">The second sequence number used for comparison</param>
        /// <returns><c>true</c> if the first sequence number is greater than the other one; otherwise <c>false</c></returns>
        public static bool operator >(SeqNo left, SeqNo right)
        {
            return left.CompareTo(right) > 0;
        }

        /// <summary>
        /// Compares two specified sequence numbers to see if the first one is greater than or equal to the other one.
        /// </summary>
        /// <param name="left">The first sequence number used for comparison</param>
        /// <param name="right">The second sequence number used for comparison</param>
        /// <returns><c>true</c> if the first sequence number is greater than or equal to the other one; otherwise <c>false</c></returns>
        public static bool operator >=(SeqNo left, SeqNo right)
        {
            return left.CompareTo(right) >= 0;
        }

        /// <summary>
        /// Performs an implicit conversion from <see cref="System.Int64"/> to <see cref="SeqNo"/>.
        /// </summary>
        /// <param name="x">The value to convert</param>
        /// <returns>The result of the conversion</returns>
        public static implicit operator SeqNo(long x)
        {
            return new SeqNo(x);
        }

        /// <summary>
        /// Performs an implicit conversion from <see cref="SeqNo"/> to <see cref="System.Int64"/>.
        /// </summary>
        /// <param name="seqNo">The sequence number to convert</param>
        /// <returns>The result of the conversion</returns>
        public static implicit operator long(SeqNo seqNo)
        {
            return seqNo.RawValue;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is SeqNo && Equals((SeqNo) obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return RawValue.GetHashCode();
        }

        #endregion


        /// <inheritdoc/>
        public override string ToString()
        {
            return RawValue.ToString(CultureInfo.InvariantCulture);
        }

        #region Static methods

        /// <summary>
        /// Implements wrap-around comparison, in the event of a 64-bit overflow
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        public class HasSeqNoComparer<T> : IComparer<T> where T : IHasSequenceNumber
        {
            /// <inheritdoc/>
            public int Compare(T x, T y)
            {
                return Comparer.Compare(x.Seq, y.Seq);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public class SeqNoComparer : IComparer<SeqNo>, IEqualityComparer<SeqNo>
        {
            /// <inheritdoc/>
            public int Compare(SeqNo x, SeqNo y)
            {
                var sgn = 0;
                if (x.RawValue < y.RawValue) sgn = -1;
                else if (x.RawValue > y.RawValue) sgn = 1;
                if (((x.RawValue - y.RawValue) * sgn) < 0L) return -sgn;
                else return sgn;
            }

            /// <inheritdoc/>
            public bool Equals(SeqNo x, SeqNo y)
            {
                return x == y;
            }

            /// <inheritdoc/>
            public int GetHashCode(SeqNo obj)
            {
                return obj.GetHashCode();
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public static readonly SeqNoComparer Comparer = new SeqNoComparer();
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="x">TBD</param>
        /// <param name="y">TBD</param>
        /// <returns>TBD</returns>
        public static int CompareSeq(SeqNo x, SeqNo y)
        {
            return Comparer.Compare(x, y);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="x">TBD</param>
        /// <param name="y">TBD</param>
        /// <returns>TBD</returns>
        public static SeqNo Max(SeqNo x, SeqNo y)
        {
            var compare = CompareSeq(x, y);
            if (compare == 0) return x;
            return compare < 0 ? y : x;
        }

        #endregion
    }

    /// <summary>
    /// Messages that are to be buffered in an <see cref="AckedSendBuffer{T}"/> or <see cref="AckedReceiveBuffer{T}"/> has
    /// to implement this interface to provide the sequence needed by the buffers
    /// </summary>
    internal interface IHasSequenceNumber
    {
        /// <summary>
        /// Sequence number of the message
        /// </summary>
        SeqNo Seq { get; }
    }

    #region AckedDelivery message types


    /// <summary>
    /// Class representing an acknowledgement with selective negative acknowledgements.
    /// </summary>
    internal sealed class Ack
    {
        /// <summary>
        /// Class representing an acknowledgement with select negative acknowledgements.
        /// </summary>
        /// <param name="cumulativeAck">Represents the highest sequence number received</param>
        /// <param name="nacks">Set of sequence numbers between the last delivered one and <paramref name="cumulativeAck"/> that has not been received.</param>
        public Ack(SeqNo cumulativeAck, IEnumerable<SeqNo> nacks)
        {
            Nacks = new SortedSet<SeqNo>(nacks, SeqNo.Comparer);
            CumulativeAck = cumulativeAck;
        }

        /// <summary>
        /// Class representing an acknowledgement with select negative acknowledgements.
        /// </summary>
        /// <param name="cumulativeAck">Represents the highest sequence number received</param>
        public Ack(SeqNo cumulativeAck) : this(cumulativeAck, new List<SeqNo>()) { }

        /// <summary>
        /// TBD
        /// </summary>
        public SeqNo CumulativeAck { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public SortedSet<SeqNo> Nacks { get; private set; }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            var nacks = string.Join(",", Nacks.Select(x => x.ToString()));
            return $"ACK[{CumulativeAck}, [{nacks}]]";
        }
    }

    /// <summary>
    /// This exception is thrown when the Resent buffer is filled beyond its capacity.
    /// </summary>
    internal class ResendBufferCapacityReachedException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ResendBufferCapacityReachedException"/> class.
        /// </summary>
        /// <param name="c">The capacity of the buffer</param>
        public ResendBufferCapacityReachedException(int c)
            : base($"Resent buffer capacity of {c} has been reached.")
        {
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="ResendBufferCapacityReachedException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected ResendBufferCapacityReachedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// This exception is thrown when the system is unable to fulfill a resend request since negatively acknowledged payload is no longer in buffer.
    /// </summary>
    internal class ResendUnfulfillableException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ResendUnfulfillableException"/> class.
        /// </summary>
        public ResendUnfulfillableException()
            : base("Unable to fulfill resend request since negatively acknowledged payload is no longer in buffer. " +
                "The resend states between two systems are compromised and cannot be recovered") { }
    }

#endregion

    /// <summary>
    /// Implements an immutable resend buffer that buffers messages until they have been acknowledged. Properly removes messages
    /// when an <see cref="Ack"/> is received. This buffer works together with <see cref="AckedReceiveBuffer{T}"/> on the receiving end.
    /// </summary>
    /// <typeparam name="T">The type of message being stored - has to implement <see cref="IHasSequenceNumber"/></typeparam>
    internal sealed class AckedSendBuffer<T> where T : IHasSequenceNumber
    {
        public AckedSendBuffer(int capacity, SeqNo maxSeq) : this(capacity, maxSeq, ImmutableList<T>.Empty, ImmutableList<T>.Empty)
        {
        }

        public AckedSendBuffer(int capacity, SeqNo maxSeq, IImmutableList<T> nacked, IImmutableList<T> nonAcked)
        {
            MaxSeq = maxSeq ?? new SeqNo(-1);
            Nacked = nacked;
            NonAcked = nonAcked;
            Capacity = capacity;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="capacity">TBD</param>
        public AckedSendBuffer(int capacity) : this(capacity, new SeqNo(-1)) { }

        /// <summary>
        /// TBD
        /// </summary>
        public int Capacity { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public IImmutableList<T> NonAcked { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public IImmutableList<T> Nacked { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public SeqNo MaxSeq { get; private set; }

        /// <summary>
        /// Processes an incoming acknowledgement and returns a new buffer with only unacknowledged elements remaining.
        /// </summary>
        /// <param name="ack">The received acknowledgement</param>
        /// <exception cref="ResendUnfulfillableException">Thrown if we couldn't fit all of the nacks stored inside <see cref="Ack"/> onto the buffer.</exception>
        /// <returns>An updated buffer containing the remaining unacknowledged messages</returns>
        public AckedSendBuffer<T> Acknowledge(Ack ack)
        {
            if (ack.CumulativeAck > MaxSeq)
            {
                throw new ArgumentException(nameof(ack), $"Highest SEQ so far was {MaxSeq} but cumulative ACK is {ack.CumulativeAck}");
            }

            var newNacked = ack.Nacks.Count == 0
                ? ImmutableList<T>.Empty
                : Nacked.AddRange(NonAcked).Where(x => ack.Nacks.Contains(x.Seq)).ToImmutableList();
            if (newNacked.Count < ack.Nacks.Count) throw new ResendUnfulfillableException();
            else return Copy(nonAcked: NonAcked.Where(x => x.Seq > ack.CumulativeAck).ToImmutableList(), nacked: newNacked);
        }

        /// <summary>
        /// Puts a new message in the buffer. 
        /// </summary>
        /// <param name="msg">The message to be stored for possible future transmission.</param>
        /// <exception cref="ArgumentException">Thrown if an out-of-sequence message is attempted to be stored.</exception>
        /// <exception cref="ResendBufferCapacityReachedException">Thrown if the resend buffer is beyond its capacity.</exception>
        /// <returns>The updated buffer.</returns>
        public AckedSendBuffer<T> Buffer(T msg)
        {
            if (msg.Seq <= MaxSeq) throw new ArgumentException($"Sequence number must be monotonic. Received {msg.Seq} which is smaller than {MaxSeq}", nameof(msg));

            if (NonAcked.Count == Capacity) throw new ResendBufferCapacityReachedException(Capacity);

            return Copy(nonAcked: NonAcked.Add(msg), maxSeq: msg.Seq);
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            var nonAcked = string.Join(",", NonAcked.Select(x => x.Seq.ToString()));
            return $"[{MaxSeq} [{nonAcked}]]";
        }

        public AckedSendBuffer<T> Copy(IImmutableList<T> nonAcked = null, IImmutableList<T> nacked = null, SeqNo maxSeq = null)
        {
            return new AckedSendBuffer<T>(Capacity, maxSeq ?? MaxSeq, nacked ?? Nacked, nonAcked ?? NonAcked);
        }

    }

    /// <summary>
    /// Helper class that makes it easier to work with <see cref="AckedReceiveBuffer{T}"/> deliverables.
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    sealed class AckReceiveDeliverable<T> where T : IHasSequenceNumber
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="buffer">TBD</param>
        /// <param name="deliverables">TBD</param>
        /// <param name="ack">TBD</param>
        public AckReceiveDeliverable(AckedReceiveBuffer<T> buffer, IReadOnlyList<T> deliverables, Ack ack)
        {
            Ack = ack;
            Deliverables = deliverables;
            Buffer = buffer;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public AckedReceiveBuffer<T> Buffer { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public IReadOnlyList<T> Deliverables { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public Ack Ack { get; private set; }
    }

    /// <summary>
    /// Implements an immutable receive buffer that buffers incoming messages until they can be safely delivered. This
    /// buffer works together with an <see cref="AckedSendBuffer{T}"/> on the sender() side.
    /// </summary>
    /// <typeparam name="T">The type of messages being buffered; must implement <see cref="IHasSequenceNumber"/>.</typeparam>
    internal sealed class AckedReceiveBuffer<T> : IEquatable<AckedReceiveBuffer<T>> where T : IHasSequenceNumber
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly SeqNo.HasSeqNoComparer<T> Comparer = new SeqNo.HasSeqNoComparer<T>();

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="lastDelivered">Sequence number of the last message that has been delivered.</param>
        /// <param name="cumulativeAck">The highest sequence number received so far</param>
        /// <param name="buffer">Buffer of messages that are waiting for delivery.</param>
        public AckedReceiveBuffer(SeqNo lastDelivered, SeqNo cumulativeAck, ImmutableSortedSet<T> buffer)
        {
            LastDelivered = lastDelivered ?? new SeqNo(-1);
            CumulativeAck = cumulativeAck ?? new SeqNo(-1);
            Buf = buffer;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public AckedReceiveBuffer()
            : this(new SeqNo(-1), new SeqNo(-1), ImmutableSortedSet<T>.Empty.WithComparer(Comparer))
        { }

        /// <summary>
        /// TBD
        /// </summary>
        public SeqNo LastDelivered { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public SeqNo CumulativeAck { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableSortedSet<T> Buf { get; }

        /// <summary>
        /// Puts a sequenced message in the receive buffer returning a new buffer.
        /// </summary>
        /// <param name="arrivedMsg">Message to be put into the buffer</param>
        /// <returns>The updated buffer containing the message</returns>
        public AckedReceiveBuffer<T> Receive(T arrivedMsg)
        {
            return Copy(cumulativeAck: SeqNo.Max(arrivedMsg.Seq, CumulativeAck),
                buffer: (arrivedMsg.Seq > LastDelivered && !Buf.Contains(arrivedMsg)) ? Buf.Add(arrivedMsg) : Buf);
        }

        /// <summary>
        /// Extract all messages that could be safely delivered, an updated ack to be sent to the sender(), and
        /// an updated buffer that has the messages removed that can be delivered.
        /// </summary>
        /// <returns>Triplet of the updated buffer, messages that can be delivered, and the updated acknowledgement.</returns>
        public AckReceiveDeliverable<T> ExtractDeliverable()
        {
                var deliver = new List<T>();
                var ack = new Ack(CumulativeAck);
                var updatedLastDelivered = LastDelivered;
                var prev = LastDelivered;

                foreach (var bufferedMessage in Buf)
                {
                    if (bufferedMessage.Seq.IsSuccessor(updatedLastDelivered))
                    {
                        deliver.Add(bufferedMessage);
                        updatedLastDelivered = updatedLastDelivered.Inc();
                    }
                    else if (!bufferedMessage.Seq.IsSuccessor(prev))
                    {
                        var nacks = new HashSet<SeqNo>();
                        unchecked //in Java, there are no overflow / underflow exceptions so the value rolls over. We have to explicitly squelch those errors in .NET
                        {
                            var diff = Math.Abs(bufferedMessage.Seq.RawValue - prev.RawValue - 1);

                            // Collect all missing sequence numbers (gaps)
                            while (diff > 0)
                            {
                                nacks.Add(prev.RawValue + diff);
                                diff--;
                            }
                        }
                        ack = new Ack(CumulativeAck, ack.Nacks.Concat(nacks));
                    }
                    prev = bufferedMessage.Seq;
                }

                var newBuf = !deliver.Any() ? Buf : Buf.Except(deliver);
                return new AckReceiveDeliverable<T>(Copy(lastDelivered: updatedLastDelivered, buffer: newBuf), deliver, ack);
        }

        /// <summary>
        /// Merges two receive buffers. Merging preserves sequencing of messages, and drops all messages that has been
        /// safely acknowledged by any of the participating buffers. Also updates the expected sequence numbers.
        /// </summary>
        /// <param name="other">The receive buffer to merge with</param>
        /// <returns>The merged receive buffer</returns>
        public AckedReceiveBuffer<T> MergeFrom(AckedReceiveBuffer<T> other)
        {
            var mergedLastDelivered = SeqNo.Max(this.LastDelivered, other.LastDelivered);
            return Copy(mergedLastDelivered, SeqNo.Max(this.CumulativeAck, other.CumulativeAck), Buf.Union(other.Buf).Where(x => x.Seq > mergedLastDelivered).ToImmutableSortedSet(Comparer));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="lastDelivered">TBD</param>
        /// <param name="cumulativeAck">TBD</param>
        /// <param name="buffer">TBD</param>
        /// <returns>TBD</returns>
        public AckedReceiveBuffer<T> Copy(SeqNo lastDelivered = null, SeqNo cumulativeAck = null, ImmutableSortedSet<T> buffer = null)
        {
            return new AckedReceiveBuffer<T>(lastDelivered ?? LastDelivered, cumulativeAck ?? CumulativeAck, buffer ?? Buf);
        }

        public bool Equals(AckedReceiveBuffer<T> other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return LastDelivered.Equals(other.LastDelivered)
                   && CumulativeAck.Equals(other.CumulativeAck);
        }

        public override bool Equals(object obj)
        {
            return ReferenceEquals(this, obj) || obj is AckedReceiveBuffer<T> other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = LastDelivered.GetHashCode();
                hashCode = (hashCode * 397) ^ CumulativeAck.GetHashCode();
                return hashCode;
            }
        }
    }
}

