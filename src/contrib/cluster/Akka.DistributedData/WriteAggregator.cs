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

namespace Akka.DistributedData
{
    internal class WriteAggregator : ReadWriteAggregator
    {
        public static Props Props(IKey key, DataEnvelope envelope, IWriteConsistency consistency, object req, IImmutableSet<Address> nodes, IActorRef replyTo) =>
            Actor.Props.Create(() => new WriteAggregator(key, envelope, consistency, req, nodes, replyTo)).WithDeploy(Deploy.Local);

        private readonly IKey _key;
        private readonly DataEnvelope _envelope;
        private readonly IWriteConsistency _consistency;
        private readonly object _req;
        private readonly IActorRef _replyTo;
        private readonly Write _write;
        
        public WriteAggregator(IKey key, DataEnvelope envelope, IWriteConsistency consistency, object req, IImmutableSet<Address> nodes, IActorRef replyTo)
            : base(nodes, consistency.Timeout)
        {
            _key = key;
            _envelope = envelope;
            _consistency = consistency;
            _req = req;
            _replyTo = replyTo;
            _write = new Write(key.Id, envelope);
        }

        protected override int DoneWhenRemainingSize
        {
            get
            {
                if (_consistency is WriteTo)
                {
                    var wt = (WriteTo)_consistency;
                    return Nodes.Count - wt.N - 1;
                }
                else if (_consistency is WriteAll)
                {
                    return 0;
                }
                else if (_consistency is WriteMajority)
                {
                    var N = Nodes.Count + 1;
                    var w = N / 2 + 1;
                    return N - w;
                }
                else if (_consistency is WriteLocal)
                {
                    throw new ArgumentException("WriteAggregator does not support ReadLocal");
                }
                else
                {
                    throw new ArgumentException("Invalid consistency level");
                }
            }
        }

        protected virtual Address SenderAddress => Sender.Path.Address;

        protected override void PreStart()
        {
            var primaryNodes = PrimaryAndSecondaryNodes.Value.Item1;
            foreach (var n in primaryNodes)
                Replica(n).Tell(_write);

            if (Remaining.Count == DoneWhenRemainingSize)
                Reply(true);
            else if (DoneWhenRemainingSize < 0 || Remaining.Count < DoneWhenRemainingSize)
                Reply(false);
        }

        protected override bool Receive(object message) => message.Match()
            .With<WriteAck>(x =>
            {
                Remaining = Remaining.Remove(SenderAddress);
                if (Remaining.Count == DoneWhenRemainingSize)
                    Reply(true);
            })
            .With<SendToSecondary>(x =>
            {
                foreach (var n in PrimaryAndSecondaryNodes.Value.Item2)
                    Replica(n).Tell(_write);
            })
            .With<ReceiveTimeout>(x => Reply(false))
            .WasHandled;

        private void Reply(bool ok)
        {
            if (ok && _envelope.Data is DeletedData)
                _replyTo.Tell(new Replicator.DeleteSuccess(_key), Context.Parent);
            else if (ok)
                _replyTo.Tell(new Replicator.UpdateSuccess(_key, _req), Context.Parent);
            else if (_envelope.Data is DeletedData)
                _replyTo.Tell(new Replicator.ReplicationDeletedFailure(_key), Context.Parent);
            else
                _replyTo.Tell(new Replicator.UpdateTimeout(_key, _req), Context.Parent);

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
    }

    public class WriteMajority : IWriteConsistency
    {
        public TimeSpan Timeout { get; }

        public WriteMajority(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        public override bool Equals(object obj)
        {
            var other = obj as WriteMajority;
            return other != null && Timeout == other.Timeout;
        }
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
    }
}
