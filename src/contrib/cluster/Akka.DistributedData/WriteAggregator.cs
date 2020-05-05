//-----------------------------------------------------------------------
// <copyright file="WriteAggregator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.DistributedData.Internal;
using System;
using System.Collections.Immutable;
using Akka.Cluster;
using Akka.Event;

namespace Akka.DistributedData
{
    internal class WriteAggregator : ReadWriteAggregator
    {
        public static Props Props(IKey key, DataEnvelope envelope, Delta delta, IWriteConsistency consistency, object req, IImmutableSet<Address> nodes, IImmutableSet<Address> unreachable, IActorRef replyTo, bool durable) =>
            Actor.Props.Create(() => new WriteAggregator(key, envelope, delta, consistency, req, nodes, unreachable, replyTo, durable)).WithDeploy(Deploy.Local);

        private readonly IKey _key;
        private readonly DataEnvelope _envelope;
        private readonly IWriteConsistency _consistency;
        private readonly object _req;
        private readonly IActorRef _replyTo;
        private readonly Write _write;
        private readonly DeltaPropagation _delta;
        private readonly bool _durable;
        private readonly UniqueAddress _selfUniqueAddress;
        private bool _gotLocalStoreReply;
        private ImmutableHashSet<Address> _gotNackFrom;

        public WriteAggregator(IKey key, DataEnvelope envelope, Delta delta, IWriteConsistency consistency, object req, IImmutableSet<Address> nodes, 
            IImmutableSet<Address> unreachable, IActorRef replyTo, bool durable)
            : base(nodes, unreachable, consistency.Timeout)
        {
            _selfUniqueAddress = Cluster.Cluster.Get(Context.System).SelfUniqueAddress;
            _key = key;
            _envelope = envelope;
            _consistency = consistency;
            _req = req;
            _replyTo = replyTo;
            _durable = durable;
            _write = new Write(key.Id, envelope);
            _delta = delta == null
                ? null
                : new DeltaPropagation(_selfUniqueAddress, true,
                    ImmutableDictionary<string, Delta>.Empty.Add(key.Id, delta));
            _gotLocalStoreReply = !durable;
            _gotNackFrom = ImmutableHashSet<Address>.Empty;
            DoneWhenRemainingSize = GetDoneWhenRemainingSize();
        }
        
        protected bool IsDone => _gotLocalStoreReply && (Remaining.Count <= DoneWhenRemainingSize || (Remaining.Except(_gotNackFrom).Count == 0) || NotEnoughNodes);

        protected bool NotEnoughNodes => DoneWhenRemainingSize < 0 || Nodes.Count < DoneWhenRemainingSize;

        protected override int DoneWhenRemainingSize { get; }

        private int GetDoneWhenRemainingSize()
        {
            switch (_consistency)
            {
                case WriteTo write: return Nodes.Count - (write.Count - 1);
                case WriteAll write: return 0;
                case WriteMajority write:
                    var n = Nodes.Count + 1;
                    var w = CalculateMajorityWithMinCapacity(write.MinCapacity, n);
                    return n - w;
                case WriteLocal write: throw new ArgumentException("WriteAggregator does not support WriteLocal");
                default: throw new ArgumentException("Invalid consistency level");
            }
        }

        protected virtual Address SenderAddress => Sender.Path.Address;

        protected override void PreStart()
        {
            var msg = (object)_delta ?? _write;
            foreach (var n in PrimaryNodes)
            {
                var replica = Replica(n);
                replica.Tell(msg);
            }

            if (IsDone) Reply(isTimeout: false);
        }

        protected override bool Receive(object message) => message.Match()
            .With<WriteAck>(x =>
            {
                Remaining = Remaining.Remove(SenderAddress);
                if (IsDone) Reply(isTimeout: false);
            })
            .With<WriteNack>(x =>
            {
                _gotNackFrom = _gotNackFrom.Add(SenderAddress);
                if (IsDone) Reply(isTimeout: false);
            })
            .With<DeltaNack>(_ =>
            {
                // ok, will be retried with full state
            })
            .With<UpdateSuccess>(x =>
            {
                _gotLocalStoreReply = true;
                if (IsDone) Reply(isTimeout: false);
            })
            .With<StoreFailure>(x =>
            {
                _gotLocalStoreReply = true;
                _gotNackFrom = _gotNackFrom.Remove(_selfUniqueAddress.Address);
                if (IsDone) Reply(isTimeout: false);
            })
            .With<SendToSecondary>(x =>
            {
                // Deltas must be applied in order and we can't keep track of ordering of
                // simultaneous updates so there is a chance that the delta could not be applied.
                // Try again with the full state to the primary nodes that have not acked.
                if (_delta != null)
                {
                    foreach (var address in PrimaryNodes.Intersect(Remaining))
                    {
                        Replica(address).Tell(_write);
                    }
                }

                foreach (var n in SecondaryNodes)
                    Replica(n).Tell(_write);
            })
            .With<ReceiveTimeout>(x => Reply(isTimeout: true))
            .WasHandled;

        private void Reply(bool isTimeout)
        {
            var notEnoughNodes = NotEnoughNodes;
            var isDelete = _envelope.Data is DeletedData;
            var done = DoneWhenRemainingSize;
            var isSuccess = Remaining.Count <= DoneWhenRemainingSize && !notEnoughNodes;
            Log.Debug("write acks remaining: {0}, done when: {1}", Remaining.Count, done);
            var isTimeoutOrNotEnoughNodes = isTimeout || notEnoughNodes || _gotNackFrom.IsEmpty;

            object reply;
            if (isSuccess && isDelete) reply = new DeleteSuccess(_key, _req);
            else if (isSuccess) reply = new UpdateSuccess(_key, _req);
            else if (isTimeoutOrNotEnoughNodes && isDelete) reply = new ReplicationDeleteFailure(_key);
            else if (isTimeoutOrNotEnoughNodes || !_durable) reply = new UpdateTimeout(_key, _req);
            else reply = new StoreFailure(_key, _req);

            _replyTo.Tell(reply, Context.Parent);
            Context.Stop(Self);
        }
    }

    public interface IWriteConsistency
    {
        TimeSpan Timeout { get; }
    }

    public sealed class WriteLocal : IWriteConsistency
    {
        public static readonly WriteLocal Instance = new WriteLocal();

        public TimeSpan Timeout => TimeSpan.Zero;

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj != null && obj is WriteLocal;
        }

        private WriteLocal() { }

        public override string ToString() => "WriteLocal";
    }

    public sealed class WriteTo : IWriteConsistency, IEquatable<WriteTo>
    {
        public int Count { get; }

        public TimeSpan Timeout { get; }

        public WriteTo(int count, TimeSpan timeout)
        {
            if (count < 2) throw new ArgumentException("WriteTo requires count > 2, Use WriteLocal for count=1");

            Count = count;
            Timeout = timeout;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as WriteTo);

        public override string ToString() => $"WriteTo({Count}, timeout={Timeout})";

        public bool Equals(WriteTo other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Count == other.Count && Timeout.Equals(other.Timeout);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Count * 397) ^ Timeout.GetHashCode();
            }
        }
    }

    public sealed class WriteMajority : IWriteConsistency, IEquatable<WriteMajority>
    {
        public TimeSpan Timeout { get; }
        public int MinCapacity { get; }

        public WriteMajority(TimeSpan timeout, int minCapacity = 0)
        {
            Timeout = timeout;
            MinCapacity = minCapacity;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as WriteMajority);

        public override string ToString() => $"WriteMajority(timeout={Timeout})";

        public bool Equals(WriteMajority other)
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

    public sealed class WriteAll : IWriteConsistency, IEquatable<WriteAll>
    {
        public TimeSpan Timeout { get; }

        public WriteAll(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as WriteAll);

        public override string ToString() => $"WriteAll(timeout={Timeout})";

        public bool Equals(WriteAll other)
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
