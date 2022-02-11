//-----------------------------------------------------------------------
// <copyright file="ReadAggregator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

        protected override bool Receive(object message) => message.Match()
            .With<ReadResult>(x =>
            {
                if (x.Envelope != null)
                {
                    _result = _result?.Merge(x.Envelope) ?? x.Envelope;
                }

                Remaining = Remaining.Remove(Sender.Path.Address);
                var done = DoneWhenRemainingSize;
                Log.Debug("read acks remaining: {0}, done when: {1}, current state: {2}", Remaining.Count, done, _result);
                if (Remaining.Count == done) Reply(true);
            })
            .With<SendToSecondary>(x =>
            {
                foreach (var n in SecondaryNodes)
                    Replica(n).Tell(_read);
            })
            .With<ReceiveTimeout>(_ => Reply(false))
            .WasHandled;

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

        private Receive WaitRepairAck(DataEnvelope envelope) => msg => msg.Match()
            .With<ReadRepairAck>(x =>
            {
                var reply = envelope.Data is DeletedData
                    ? (object)new DataDeleted(_key, null)
                    : new GetSuccess(_key, _req, envelope.Data);
                _replyTo.Tell(reply, Context.Parent);
                Context.Stop(Self);
            })
            .With<ReadResult>(x => Remaining = Remaining.Remove(Sender.Path.Address))
            .With<SendToSecondary>(_ => { })
            .With<ReceiveTimeout>(_ => { })
            .WasHandled;
    }

    public interface IReadConsistency
    {
        TimeSpan Timeout { get; }
    }

    public sealed class ReadLocal : IReadConsistency
    {
        public static readonly ReadLocal Instance = new ReadLocal();

        public TimeSpan Timeout => TimeSpan.Zero;

        private ReadLocal() { }

        
        public override bool Equals(object obj) => obj != null && obj is ReadLocal;

        
        public override string ToString() => "ReadLocal";
        
        public override int GetHashCode() => nameof(ReadLocal).GetHashCode();
    }

    public sealed class ReadFrom : IReadConsistency, IEquatable<ReadFrom>
    {
        public int N { get; }

        public TimeSpan Timeout { get; }

        public ReadFrom(int n, TimeSpan timeout)
        {
            N = n;
            Timeout = timeout;
        }

        
        public override bool Equals(object obj) => obj is ReadFrom && Equals((ReadFrom) obj);

        
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

    public sealed class ReadMajority : IReadConsistency, IEquatable<ReadMajority>
    {
        public TimeSpan Timeout { get; }
        public int MinCapacity { get; }

        public ReadMajority(TimeSpan timeout, int minCapacity = 0)
        {
            Timeout = timeout;
            MinCapacity = minCapacity;
        }

        
        public override bool Equals(object obj)
        {
            return obj is ReadMajority && Equals((ReadMajority) obj);
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
    /// <see cref="ReadMajority"/> but with the given number of <see cref="Additional"/> nodes added to the majority count. At most
    /// all nodes. Exiting nodes are excluded using `ReadMajorityPlus` because those are typically
    /// about to be removed and will not be able to respond.
    /// </summary>
    public sealed class ReadMajorityPlus : IReadConsistency, IEquatable<ReadMajorityPlus>
    {
        public TimeSpan Timeout { get; }
        public int Additional { get; }
        public int MinCapacity { get; }

        public ReadMajorityPlus(TimeSpan timeout, int additional, int minCapacity = 0)
        {
            Timeout = timeout;
            Additional = additional;
            MinCapacity = minCapacity;
        }

        
        public override bool Equals(object obj)
        {
            return obj is ReadMajorityPlus && Equals((ReadMajorityPlus)obj);
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

    public sealed class ReadAll : IReadConsistency, IEquatable<ReadAll>
    {
        public TimeSpan Timeout { get; }

        public ReadAll(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        
        public override bool Equals(object obj)
        {
            return obj is ReadAll && Equals((ReadAll) obj);
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
