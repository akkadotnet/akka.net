//-----------------------------------------------------------------------
// <copyright file="ReadAggregator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.DistributedData.Internal;
using System;
using System.Collections.Immutable;
using Akka.Event;

namespace Akka.DistributedData
{
    internal class ReadAggregator : ReadWriteAggregator
    {
        internal static Props Props(IKey key, IReadConsistency consistency, object req, IImmutableList<Address> nodes, IImmutableSet<Address> unreachable, bool shuffle, DataEnvelope localValue, IActorRef replyTo) =>
            Actor.Props.Create(() => new ReadAggregator(key, consistency, req, nodes, unreachable, shuffle, localValue, replyTo)).WithDeploy(Deploy.Local);

        private readonly IKey _key;
        private readonly IReadConsistency _consistency;
        private readonly object _req;
        private readonly IActorRef _replyTo;
        private readonly Read _read;

        private DataEnvelope _result;

        public ReadAggregator(IKey key, IReadConsistency consistency, object req, IImmutableList<Address> nodes, IImmutableSet<Address> unreachable, bool shuffle, DataEnvelope localValue, IActorRef replyTo)
            : base(nodes, unreachable, consistency.Timeout, shuffle)
        {
            _key = key;
            _consistency = consistency;
            _req = req;
            _replyTo = replyTo;
            _result = localValue;
            _read = new Read(key.Id);
            DoneWhenRemainingSize = GetDoneWhenRemainingSize();
        }
        protected override int DoneWhenRemainingSize { get; }

        private int GetDoneWhenRemainingSize()
        {
            switch (_consistency)
            {
                case ReadFrom read: return Nodes.Count - (read.N - 1);
                case ReadAll _: return 0;
                case ReadMajority read:
                    {
                        // +1 because local node is not included in 'Nodes'
                        var n = Nodes.Count + 1;
                        var r = CalculateMajority(read.MinCapacity, n, 0);
                        Log.Debug("ReadMajority [{0}] [{1}] of [{2}].", _key, r, n);
                        return n - r;
                    }
                case ReadMajorityPlus read:
                    {
                        // +1 because local node is not included in 'Nodes'
                        var n = Nodes.Count + 1;
                        var r = CalculateMajority(read.MinCapacity, n, read.Additional);
                        Log.Debug("ReadMajorityPlus [{0}] [{1}] of [{2}].", _key, r, n);
                        return n - r;
                    }
                case ReadLocal _: throw new ArgumentException("ReadAggregator does not support ReadLocal");
                default: throw new ArgumentException("Invalid consistency level");
            }
        }

        protected override void PreStart()
        {
            foreach (var n in PrimaryNodes)
            {
                var replica = Replica(n);
                Log.Debug("Sending {0} to primary replica {1}", _read, replica);
                replica.Tell(_read);
            }

            if (Remaining.Count == DoneWhenRemainingSize)
                Reply(true);
            else if (DoneWhenRemainingSize < 0 || Remaining.Count < DoneWhenRemainingSize)
                Reply(false);
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case ReadResult x:
                    if (x.Envelope != null)
                    {
                        _result = _result?.Merge(x.Envelope) ?? x.Envelope;
                    }

                    Remaining = Remaining.Remove(Sender.Path.Address);
                    var done = DoneWhenRemainingSize;
                    Log.Debug("read acks remaining: {0}, done when: {1}, current state: {2}", Remaining.Count, done,
                        _result);
                    if (Remaining.Count == done) Reply(true);
                    return true;
                
                case SendToSecondary x:
                    foreach (var n in SecondaryNodes)
                        Replica(n).Tell(_read);
                    return true;
                
                case ReceiveTimeout _:
                    Reply(false);
                    return true;
                
                default:
                    return false;
            }
        }

        private void Reply(bool ok)
        {
            if (ok && _result != null)
            {
                Context.Parent.Tell(new ReadRepair(_key.Id, _result));
                Context.Become(WaitRepairAck(_result));
            }
            else if (ok && _result == null)
            {
                _replyTo.Tell(new NotFound(_key, _req), Context.Parent);
                Context.Stop(Self);
            }
            else
            {
                _replyTo.Tell(new GetFailure(_key, _req), Context.Parent);
                Context.Stop(Self);
            }
        }

        private Receive WaitRepairAck(DataEnvelope envelope) => msg =>
        {
            switch (msg)
            {
                case ReadRepairAck _:
                    var reply = envelope.Data is DeletedData
                        ? (object)new DataDeleted(_key, null)
                        : new GetSuccess(_key, _req, envelope.Data);
                    _replyTo.Tell(reply, Context.Parent);
                    Context.Stop(Self);
                    return true;
                case ReadResult _:
                    Remaining = Remaining.Remove(Sender.Path.Address);
                    return true;
                case SendToSecondary _:
                    // no-op
                    return true;
                case ReceiveTimeout _:
                    // no-op
                    return true;
                default:
                    return false;
            }
        };
    }

    /// <summary>
    /// Specifies the read consistency level used by a <see cref="Get"/> request.
    /// See <see cref="Replicator"/> for an overview of the available read consistency levels.
    /// </summary>
    public interface IReadConsistency
    {
        /// <summary>
        /// Gets the maximum time to wait for the requested read consistency level to be reached.
        /// </summary>
        TimeSpan Timeout { get; }
    }

    /// <summary>
    /// Reads a value from the local replica only. This is the default consistency level for
    /// <see cref="DistributedData.GetAsync{T}(IKey{T},IReadConsistency,System.Threading.CancellationToken)"/>.
    /// </summary>
    public sealed class ReadLocal : IReadConsistency
    {
        /// <summary>
        /// Gets the singleton local read consistency level.
        /// </summary>
        public static readonly ReadLocal Instance = new();

        /// <summary>
        /// Gets the timeout for this consistency level, which is always zero because it reads only local state.
        /// </summary>
        public TimeSpan Timeout => TimeSpan.Zero;

        private ReadLocal() { }

        
        public override bool Equals(object obj) => obj != null && obj is ReadLocal;

        
        public override string ToString() => "ReadLocal";
        
        public override int GetHashCode() => nameof(ReadLocal).GetHashCode();
    }

    /// <summary>
    /// Reads and merges values from the requested number of replicas in total, including the local replica.
    /// See <see cref="Replicator"/> for the read consistency overview.
    /// </summary>
    public sealed class ReadFrom : IReadConsistency, IEquatable<ReadFrom>
    {
        /// <summary>
        /// Gets the number of replicas whose values are included in the read, counting the local replica.
        /// </summary>
        public int N { get; }

        /// <summary>
        /// Gets the maximum time to wait for the requested replica responses.
        /// </summary>
        public TimeSpan Timeout { get; }

        /// <summary>
        /// Initializes a read that waits for values from <paramref name="n"/> replicas, including the local replica.
        /// </summary>
        /// <param name="n">The number of replicas whose values are included in the read, counting the local replica.</param>
        /// <param name="timeout">The maximum time to wait for the requested replica responses.</param>
        public ReadFrom(int n, TimeSpan timeout)
        {
            N = n;
            Timeout = timeout;
        }

        
        public override bool Equals(object obj) => obj is ReadFrom from && Equals(from);

        
        public bool Equals(ReadFrom other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;
            return N == other.N && Timeout.Equals(other.Timeout);
        }

        
        public override int GetHashCode()
        {
            unchecked
            {
                return (N * 397) ^ Timeout.GetHashCode();
            }
        }

        
        public override string ToString() => $"ReadFrom({N}, timeout={Timeout})";
    }

    /// <summary>
    /// Reads and merges values from a majority of replicas. The required response count is the larger of the
    /// majority count and <see cref="MinCapacity"/>, capped at the number of replicas selected for the request.
    /// See <see cref="Replicator"/> for the read consistency overview.
    /// </summary>
    public sealed class ReadMajority : IReadConsistency, IEquatable<ReadMajority>
    {
        /// <summary>
        /// Gets the maximum time to wait for the required replica responses.
        /// </summary>
        public TimeSpan Timeout { get; }

        /// <summary>
        /// Gets the minimum response count used in the calculation. The final response count is capped at the
        /// number of replicas selected for the request.
        /// </summary>
        public int MinCapacity { get; }

        /// <summary>
        /// Initializes a majority read.
        /// </summary>
        /// <param name="timeout">The maximum time to wait for the required replica responses.</param>
        /// <param name="minCapacity">The minimum response count used in the calculation; the final response count is capped at the number of replicas selected for the request.</param>
        public ReadMajority(TimeSpan timeout, int minCapacity = 0)
        {
            Timeout = timeout;
            MinCapacity = minCapacity;
        }

        
        public override bool Equals(object obj)
        {
            return obj is ReadMajority majority && Equals(majority);
        }

        
        public override string ToString() => $"ReadMajority(timeout={Timeout})";

        
        public bool Equals(ReadMajority other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Timeout.Equals(other.Timeout) && MinCapacity == other.MinCapacity;
        }

        
        public override int GetHashCode()
        {
            unchecked
            {
                return (Timeout.GetHashCode() * 397) ^ MinCapacity;
            }
        }
    }

    /// <summary>
    /// Adjusts the majority response count by <see cref="Additional"/>. The greater of that adjusted count and
    /// <see cref="MinCapacity"/> is capped at the number of replicas selected for the request. Exiting nodes
    /// are excluded from this read, since they may not be able to respond.
    /// </summary>
    public sealed class ReadMajorityPlus : IReadConsistency, IEquatable<ReadMajorityPlus>
    {
        /// <summary>
        /// Gets the maximum time to wait for the required replica responses.
        /// </summary>
        public TimeSpan Timeout { get; }

        /// <summary>
        /// Gets the adjustment applied to the majority response count before <see cref="MinCapacity"/> is applied
        /// and the result is capped at the number of replicas selected for the request.
        /// </summary>
        public int Additional { get; }

        /// <summary>
        /// Gets the minimum response count used in the calculation. The final response count is capped at the
        /// number of replicas selected for the request.
        /// </summary>
        public int MinCapacity { get; }

        /// <summary>
        /// Initializes a majority read with an adjusted response count.
        /// </summary>
        /// <param name="timeout">The maximum time to wait for the required replica responses.</param>
        /// <param name="additional">The adjustment to the majority response count.</param>
        /// <param name="minCapacity">The minimum response count used in the calculation; the final response count is capped at the number of replicas selected for the request.</param>
        public ReadMajorityPlus(TimeSpan timeout, int additional, int minCapacity = 0)
        {
            Timeout = timeout;
            Additional = additional;
            MinCapacity = minCapacity;
        }

        
        public override bool Equals(object obj)
        {
            return obj is ReadMajorityPlus plus && Equals(plus);
        }

        
        public override string ToString() => $"ReadMajorityPlus(timeout={Timeout}, additional={Additional})";

        
        public bool Equals(ReadMajorityPlus other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Timeout.Equals(other.Timeout) && Additional == other.Additional && MinCapacity == other.MinCapacity;
        }

        
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = 13;
                hashCode = (hashCode * 397) ^ Timeout.GetHashCode();
                hashCode = (hashCode * 397) ^ Additional.GetHashCode();
                hashCode = (hashCode * 397) ^ MinCapacity.GetHashCode();
                return hashCode;
            }
        }
    }

    /// <summary>
    /// Reads and merges values from every replica selected for the request. Exiting nodes are excluded.
    /// See <see cref="Replicator"/> for the read consistency overview.
    /// </summary>
    public sealed class ReadAll : IReadConsistency, IEquatable<ReadAll>
    {
        /// <summary>
        /// Gets the maximum time to wait for responses from all replicas selected for the request.
        /// </summary>
        public TimeSpan Timeout { get; }

        /// <summary>
        /// Initializes a read that waits for responses from all replicas selected for the request.
        /// </summary>
        /// <param name="timeout">The maximum time to wait for responses from all replicas selected for the request.</param>
        public ReadAll(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        
        public override bool Equals(object obj)
        {
            return obj is ReadAll all && Equals(all);
        }

        
        public override string ToString() => $"ReadAll(timeout={Timeout})";

        
        public bool Equals(ReadAll other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Timeout.Equals(other.Timeout);
        }

        
        public override int GetHashCode()
        {
            return Timeout.GetHashCode();
        }
    }
}
