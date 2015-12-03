//-----------------------------------------------------------------------
// <copyright file="AckedDelivery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
        public SeqNo(long rawValue)
        {
            RawValue = rawValue;
        }

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

        public int CompareTo(SeqNo other)
        {
            return CompareSeq(this, other);
        }

        #endregion

        #region Operators / Equality

        public static bool operator <(SeqNo s1, SeqNo s2)
        {
            return s1.CompareTo(s2) < 0;
        }

        public static bool operator <=(SeqNo s1, SeqNo s2)
        {
            return s1.CompareTo(s2) <= 0;
        }

        public bool Equals(SeqNo other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return RawValue == other.RawValue;
        }

        public static bool operator ==(SeqNo left, SeqNo right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(SeqNo left, SeqNo right)
        {
            return !Equals(left, right);
        }

        public static bool operator >(SeqNo s1, SeqNo s2)
        {
            return s1.CompareTo(s2) > 0;
        }

        public static bool operator >=(SeqNo s1, SeqNo s2)
        {
            return s1.CompareTo(s2) >= 0;
        }

        public static implicit operator SeqNo(long x)
        {
            return new SeqNo(x);
        }

        public static implicit operator long(SeqNo x)
        {
            return x.RawValue;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is SeqNo && Equals((SeqNo) obj);
        }

        public override int GetHashCode()
        {
            return RawValue.GetHashCode();
        }

        #endregion

        public override string ToString()
        {
            return RawValue.ToString(CultureInfo.InvariantCulture);
        }

        #region Static methods

        /// <summary>
        /// Implements wrap-around comparison, in the event of a 64-bit overflow
        /// </summary>
        public class HasSeqNoComparer<T> : IComparer<T> where T : IHasSequenceNumber
        {
            public int Compare(T x, T y)
            {
                return Comparer.Compare(x.Seq, y.Seq);
            }
        }

        public class SeqNoComparer : IComparer<SeqNo>, IEqualityComparer<SeqNo>
        {
            public int Compare(SeqNo x, SeqNo y)
            {
                var sgn = 0;
                if (x.RawValue < y.RawValue) sgn = -1;
                else if (x.RawValue > y.RawValue) sgn = 1;
                if (((x.RawValue - y.RawValue) * sgn) < 0L) return -sgn;
                else return sgn;
            }

            public bool Equals(SeqNo x, SeqNo y)
            {
                return x == y;
            }

            public int GetHashCode(SeqNo obj)
            {
                return obj.GetHashCode();
            }
        }

        public static readonly SeqNoComparer Comparer = new SeqNoComparer();
        public static int CompareSeq(SeqNo x, SeqNo y)
        {
            return Comparer.Compare(x, y);
        }

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


    sealed class Ack
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

        public SeqNo CumulativeAck { get; private set; }

        public SortedSet<SeqNo> Nacks { get; private set; }

        public override string ToString()
        {
            return string.Format("ACK[{0}, {1}]", CumulativeAck, String.Join(",", Nacks.Select(x => x.ToString())));
        }
    }

    /// <summary>
    /// This exception is thrown when the Resent buffer is filled beyond its capacity.
    /// </summary>
    class ResendBufferCapacityReachedException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ResendBufferCapacityReachedException"/> class.
        /// </summary>
        /// <param name="c">The capacity of the buffer</param>
        public ResendBufferCapacityReachedException(int c)
            : base(string.Format("Resent buffer capacity of {0} has been reached.", c))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ResendBufferCapacityReachedException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected ResendBufferCapacityReachedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// This exception is thrown when the system is unable to fulfill a resend request since negatively acknowledged payload is no longer in buffer.
    /// </summary>
    class ResendUnfulfillableException : AkkaException
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
    sealed class AckedSendBuffer<T> where T : IHasSequenceNumber
    {
        public AckedSendBuffer(int capacity, SeqNo maxSeq)
        {
            MaxSeq = maxSeq ?? new SeqNo(-1);
            Nacked = new List<T>();
            NonAcked = new List<T>();
            Capacity = capacity;
        }

        public AckedSendBuffer(int capacity) : this(capacity, new SeqNo(-1)) { }

        public int Capacity { get; private set; }

        public List<T> NonAcked { get; private set; }

        public List<T> Nacked { get; private set; }

        public SeqNo MaxSeq { get; private set; }

        /// <summary>
        /// Processes an incoming acknowledgement and returns a new buffer with only unacknowledged elements remaining.
        /// </summary>
        /// <param name="ack">The received acknowledgement</param>
        /// <exception cref="ResendUnfulfillableException">Thrown if we couldn't fit all of the nacks stored inside <see cref="Ack"/> onto the buffer.</exception>
        /// <returns>An updated buffer containing the remaining unacknowledged messages</returns>
        public AckedSendBuffer<T> Acknowledge(Ack ack)
        {
            var newNacked = new List<T>(Nacked.Concat(NonAcked)).Where(x => ack.Nacks.Contains(x.Seq)).ToList();
            if (newNacked.Count < ack.Nacks.Count) throw new ResendUnfulfillableException();
            else return Copy(nonAcked: NonAcked.Where(x => x.Seq > ack.CumulativeAck).ToList(), nacked: newNacked);
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
            if (msg.Seq <= MaxSeq) throw new ArgumentException(String.Format("Sequence number must be monotonic. Received {0} which is smaller than {1}", msg.Seq, MaxSeq));

            if (NonAcked.Count == Capacity) throw new ResendBufferCapacityReachedException(Capacity);

            return Copy(nonAcked: new List<T>(NonAcked) { msg }, maxSeq: msg.Seq);
        }

        public override string ToString()
        {
            return string.Format("[{0}]", string.Join(",", NonAcked.Select(x => x.ToString())));
        }

        #region Copy methods

        public AckedSendBuffer<T> Copy(List<T> nonAcked = null, List<T> nacked = null, SeqNo maxSeq = null)
        {
            return new AckedSendBuffer<T>(Capacity, maxSeq ?? MaxSeq) { Nacked = nacked ?? Nacked.ToArray().ToList(), NonAcked = nonAcked ?? NonAcked.ToArray().ToList() };
        }

        #endregion
    }

    /// <summary>
    /// Helper class that makes it easier to work with <see cref="AckedReceiveBuffer{T}"/> deliverables.
    /// </summary>
    sealed class AckReceiveDeliverable<T> where T:IHasSequenceNumber
    {
        public AckReceiveDeliverable(AckedReceiveBuffer<T> buffer, List<T> deliverables, Ack ack)
        {
            Ack = ack;
            Deliverables = deliverables;
            Buffer = buffer;
        }

        public AckedReceiveBuffer<T> Buffer { get; private set; }

        public List<T> Deliverables { get; private set; }

        public Ack Ack { get; private set; }
    }

    /// <summary>
    /// Implements an immutable receive buffer that buffers incoming messages until they can be safely delivered. This
    /// buffer works together with an <see cref="AckedSendBuffer{T}"/> on the sender() side.
    /// </summary>
    /// <typeparam name="T">The type of messages being buffered; must implement <see cref="IHasSequenceNumber"/>.</typeparam>
    sealed class AckedReceiveBuffer<T> where T : IHasSequenceNumber
    {
        public readonly static SeqNo.HasSeqNoComparer<T> Comparer = new SeqNo.HasSeqNoComparer<T>();

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="lastDelivered">Sequence number of the last message that has been delivered.</param>
        /// <param name="cumulativeAck">The highest sequence number received so far</param>
        /// <param name="buffer">Buffer of messages that are waiting for delivery.</param>
        public AckedReceiveBuffer(SeqNo lastDelivered, SeqNo cumulativeAck, SortedSet<T> buffer)
        {
            LastDelivered = lastDelivered ?? new SeqNo(-1);
            CumulativeAck = cumulativeAck ?? new SeqNo(-1);
            Buf = buffer;
        }

        public AckedReceiveBuffer()
            : this(new SeqNo(-1), new SeqNo(-1), new SortedSet<T>(Comparer))
        { }

        public SeqNo LastDelivered { get; private set; }

        public SeqNo CumulativeAck { get; private set; }

        public SortedSet<T> Buf { get; private set; }

        /// <summary>
        /// Puts a sequenced message in the receive buffer returning a new buffer.
        /// </summary>
        /// <param name="arrivedMsg">Message to be put into the buffer</param>
        /// <returns>The updated buffer containing the message</returns>
        public AckedReceiveBuffer<T> Receive(T arrivedMsg)
        {
            if (arrivedMsg.Seq > LastDelivered && !Buf.Contains(arrivedMsg))
            {
                Buf.Add(arrivedMsg);
            }
            return Copy(cumulativeAck: SeqNo.Max(arrivedMsg.Seq, CumulativeAck),
                buffer: Buf);
        }

        /// <summary>
        /// Extract all messages that could be safely delivered, an updated ack to be sent to the sender(), and
        /// an updated buffer that has the messages removed that can be delivered.
        /// </summary>
        /// <returns>Triplet of the updated buffer, messages that can be delivered, and the updated acknowledgement.</returns>
        public AckReceiveDeliverable<T> ExtractDeliverable
        {
            get
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

                Buf.ExceptWith(deliver);
                return new AckReceiveDeliverable<T>(Copy(lastDelivered: updatedLastDelivered, buffer: Buf), deliver, ack);
            }
            
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
            Buf.UnionWith(other.Buf);
            Buf.RemoveWhere(x => x.Seq < mergedLastDelivered);
            return Copy(mergedLastDelivered, SeqNo.Max(this.CumulativeAck, other.CumulativeAck), Buf);
        }

        #region Copy methods

        public AckedReceiveBuffer<T> Copy(SeqNo lastDelivered = null, SeqNo cumulativeAck = null, SortedSet<T> buffer = null)
        {
            return new AckedReceiveBuffer<T>(lastDelivered ?? LastDelivered, cumulativeAck ?? CumulativeAck, buffer ?? new SortedSet<T>(Buf, Comparer));
        }

        #endregion
    }
}

