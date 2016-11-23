//-----------------------------------------------------------------------
// <copyright file="ReadAggregator.cs" company="Akka.NET Project">
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
    internal class ReadAggregator : ReadWriteAggregator
    {
        internal static Props Props(IKey key, IReadConsistency consistency, object req, IImmutableSet<Address> nodes, DataEnvelope localValue, IActorRef replyTo) =>
            Actor.Props.Create(() => new ReadAggregator(key, consistency, req, nodes, localValue, replyTo)).WithDeploy(Deploy.Local);

        private readonly IKey _key;
        private readonly IReadConsistency _consistency;
        private readonly object _req;
        private readonly IActorRef _replyTo;
        private readonly Read _read;
        
        private DataEnvelope _result;

        public ReadAggregator(IKey key, IReadConsistency consistency, object req, IImmutableSet<Address> nodes, DataEnvelope localValue, IActorRef replyTo)
            : base(nodes, consistency.Timeout)
        {
            _key = key;
            _consistency = consistency;
            _req = req;
            _replyTo = replyTo;
            _result = localValue;
            _read = new Read(key.Id);
        }

        protected override int DoneWhenRemainingSize
        {
            get
            {
                if (_consistency is ReadFrom)
                {
                    var wt = (ReadFrom)_consistency;
                    return Nodes.Count - wt.N - 1;
                }
                else if (_consistency is ReadAll)
                {
                    return 0;
                }
                else if (_consistency is ReadMajority)
                {
                    var N = Nodes.Count + 1;
                    var w = N / 2 + 1;
                    return N - w;
                }
                else if (_consistency is ReadLocal)
                {
                    throw new ArgumentException("ReadAggregator does not support ReadLocal");
                }
                else
                {
                    throw new ArgumentException("Invalid consistency level");
                }
            }
        }

        protected override void PreStart()
        {
            foreach (var n in PrimaryAndSecondaryNodes.Value.Item1)
                Replica(n).Tell(_read);

            if (Remaining.Count == DoneWhenRemainingSize)
                Reply(true);
            else if (DoneWhenRemainingSize < 0 || Remaining.Count < DoneWhenRemainingSize)
                Reply(false);
        }

        protected override bool Receive(object message) => message.Match()
            .With<ReadResult>(x =>
            {
                if (_result != null && x.Envelope != null) _result = _result.Merge(x.Envelope.Data);
                else if (_result == null && x.Envelope != null) _result = x.Envelope;
                else if (_result != null && x.Envelope == null) _result = _result;
                else _result = null;

                Remaining = Remaining.Remove(Sender.Path.Address);
                if (Remaining.Count == DoneWhenRemainingSize) Reply(true);
            })
            .With<SendToSecondary>(x =>
            {
                foreach (var n in PrimaryAndSecondaryNodes.Value.Item2)
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
            if (ok && _result == null)
            {
                _replyTo.Tell(new Replicator.NotFound(_key, _req), Context.Parent);
                Context.Stop(Self);
            }
            else
            {
                _replyTo.Tell(new Replicator.GetFailure(_key, _req), Context.Parent);
                Context.Stop(Self);
            }
        }

        private Receive WaitRepairAck(DataEnvelope envelope) => msg => msg.Match()
            .With<ReadRepairAck>(x =>
            {
                var reply = envelope.Data is DeletedData
                    ? (object)new Replicator.DataDeleted(_key)
                    : new Replicator.GetSuccess(_key, _req, envelope.Data);
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
    }

    public sealed class ReadFrom : IReadConsistency
    {
        public int N { get; }

        public TimeSpan Timeout { get; }

        public ReadFrom(int n, TimeSpan timeout)
        {
            N = n;
            Timeout = timeout;
        }

        public override bool Equals(object obj)
        {
            var other = obj as ReadFrom;
            return other != null && (N == other.N && Timeout.Equals(other.Timeout));
        }
    }

    public sealed class ReadMajority : IReadConsistency
    {
        public TimeSpan Timeout { get; }

        public ReadMajority(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        public override bool Equals(object obj)
        {
            var other = obj as ReadMajority;
            return other != null && Timeout == other.Timeout;
        }
    }

    public sealed class ReadAll : IReadConsistency
    {
        public TimeSpan Timeout { get; }

        public ReadAll(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        public override bool Equals(object obj)
        {
            var other = obj as ReadAll;
            return other != null && Timeout == other.Timeout;
        }
    }
}
