//-----------------------------------------------------------------------
// <copyright file="WriteAggregator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.DistributedData.Internal;
using System;
using System.Collections.Immutable;
using Akka.Event;

namespace Akka.DistributedData
{
    internal class WriteAggregator : ReadWriteAggregator
    {
        public static Props Props(IKey key, DataEnvelope envelope, IWriteConsistency consistency, object req, IImmutableSet<Address> nodes, IImmutableSet<Address> unreachable, IActorRef replyTo, bool durable) =>
            Actor.Props.Create(() => new WriteAggregator(key, envelope, consistency, req, nodes, unreachable, replyTo, durable)).WithDeploy(Deploy.Local);
        
        private readonly IKey _key;
        private readonly DataEnvelope _envelope;
        private readonly IWriteConsistency _consistency;
        private readonly object _req;
        private readonly IActorRef _replyTo;
        private readonly Write _write;
        private readonly bool _durable;
        private bool _gotLocalStoreReply;
        private ImmutableHashSet<Address> _gotNackFrom;

        public WriteAggregator(IKey key, DataEnvelope envelope, IWriteConsistency consistency, object req, IImmutableSet<Address> nodes, IImmutableSet<Address> unreachable, IActorRef replyTo, bool durable)
            : base(nodes, unreachable, consistency.Timeout)
        {
            _key = key;
            _envelope = envelope;
            _consistency = consistency;
            _req = req;
            _replyTo = replyTo;
            _durable = durable;
            _write = new Write(key.Id, envelope);
            _gotLocalStoreReply = !durable;
            _gotNackFrom =ImmutableHashSet<Address>.Empty;
            DoneWhenRemainingSize = GetDoneWhenRemainingSize();
        }

        protected bool IsDone => _gotLocalStoreReply && (Remaining.Count <= DoneWhenRemainingSize || (Remaining.Except(_gotNackFrom).Count == 0) || NotEnoughNodes);

        protected bool NotEnoughNodes => DoneWhenRemainingSize < 0 || Nodes.Count < DoneWhenRemainingSize;

        protected override int DoneWhenRemainingSize { get; } 

        private int GetDoneWhenRemainingSize()
        {
            if (_consistency is WriteTo) return Nodes.Count - (((WriteTo) _consistency).N - 1);
            else if (_consistency is WriteAll) return 0;
            else if (_consistency is WriteMajority)
            {
                var consistency = (WriteMajority) _consistency;
                var N = Nodes.Count + 1;
                var w = CalculateMajorityWithMinCapacity(consistency.MinCapacity, N);
                return N - w;
            }
            else if (_consistency is WriteLocal) throw new ArgumentException("WriteAggregator does not support WriteLocal");
            else throw new ArgumentException("Invalid consistency level");
        }

        protected virtual Address SenderAddress => Sender.Path.Address;

        protected override void PreStart()
        {
            foreach (var n in PrimaryAndSecondaryNodes.Value.Item1) Replica(n).Tell(_write);

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
                _gotNackFrom = _gotNackFrom.Remove(SenderAddress);
                if (IsDone) Reply(isTimeout: false);
            })
            .With<Replicator.UpdateSuccess>(x =>
            {
                _gotLocalStoreReply = true;
                if (IsDone) Reply(isTimeout: false);
            })
            .With<Replicator.StoreFailure>(x =>
            {
                _gotLocalStoreReply = true;
                _gotNackFrom = _gotNackFrom.Remove(Cluster.Cluster.Get(Context.System).SelfAddress);
                if (IsDone) Reply(isTimeout: false);
            })
            .With<SendToSecondary>(x =>
            {
                foreach (var n in PrimaryAndSecondaryNodes.Value.Item2)
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
            Context.GetLogger().Debug("remaining: {0}, done when: {1}", Remaining.Count, done);
            var isTimeoutOrNotEnoughNodes = isTimeout || notEnoughNodes || _gotNackFrom.IsEmpty;

            object reply;
            if (isSuccess && isDelete) reply = new Replicator.DeleteSuccess(_key, _req);
            else if (isSuccess) reply = new Replicator.UpdateSuccess(_key, _req);
            else if (isTimeoutOrNotEnoughNodes && isDelete) reply = new Replicator.ReplicationDeletedFailure(_key);
            else if (isTimeoutOrNotEnoughNodes) reply = new Replicator.UpdateTimeout(_key, _req);
            else reply = new Replicator.StoreFailure(_key, _req);

            _replyTo.Tell(reply, Context.Parent);
            Context.Stop(Self);
        }
    }

    public interface IWriteConsistency
    {
        TimeSpan Timeout { get; }
    }

    public class WriteLocal : IWriteConsistency
    {
        public static readonly WriteLocal Instance = new WriteLocal();

        public TimeSpan Timeout => TimeSpan.Zero;

        public override bool Equals(object obj)
        {
            return obj != null && obj is WriteLocal;
        }

        private WriteLocal() { }

        public override string ToString() => "WriteLocal";
    }

    public class WriteTo : IWriteConsistency
    {
        public int N { get; }

        public TimeSpan Timeout { get; }

        public WriteTo(int n, TimeSpan timeout)
        {
            if (n < 2) throw new ArgumentException("WriteTo requires n > 2, Use WriteLocal for n=1");

            N = n;
            Timeout = timeout;
        }

        public override bool Equals(object obj)
        {
            var other = obj as WriteTo;
            return other != null && (N == other.N && Timeout == other.Timeout);
        }

        public override string ToString() => $"WriteTo({N})";
    }

    public class WriteMajority : IWriteConsistency
    {
        public TimeSpan Timeout { get; }
        public int MinCapacity { get; }

        public WriteMajority(TimeSpan timeout, int minCapacity = 0)
        {
            Timeout = timeout;
            MinCapacity = minCapacity;
        }

        public override bool Equals(object obj)
        {
            var other = obj as WriteMajority;
            return other != null && Timeout == other.Timeout && MinCapacity == other.MinCapacity;
        }

        public override string ToString() => "WriteMajority";
    }

    public class WriteAll : IWriteConsistency
    {
        public TimeSpan Timeout { get; }

        public WriteAll(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        public override bool Equals(object obj)
        {
            var other = obj as WriteAll;
            return other != null && Timeout == other.Timeout;
        }

        public override string ToString() => "WriteAll";
    }
}
