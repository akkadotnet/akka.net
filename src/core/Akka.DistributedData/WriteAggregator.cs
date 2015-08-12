using Akka.Actor;
using Akka.DistributedData.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    internal class WriteAggregator : ReadWriteAggregator
    {
        readonly Key<IReplicatedData> _key;
        readonly DataEnvelope _envelope;
        readonly IWriteConsistency _consistency;
        readonly object _req;
        readonly IActorRef _replyTo;
        readonly Write _write;

        public static Props GetProps(Key<IReplicatedData> key, DataEnvelope envelope, IWriteConsistency consistency, Object req, IImmutableSet<Address> nodes, IActorRef replyTo)
        {
            return Props.Create(() => new WriteAggregator(key, envelope, consistency, req, nodes, replyTo)).WithDeploy(Deploy.Local);
        }

        protected override TimeSpan Timeout
        {
            get { return _consistency.Timeout; }
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

        protected override void PreStart()
        {
            var primaryNodes = _primaryAndSecondaryNodes.Value.Item1;
            foreach(var n in primaryNodes)
            {
                Replica(n).Tell(_write);
            }
            if(_remaining.Count == DoneWhenRemainingSize)
            {
                Reply(true);
            }
            else if(DoneWhenRemainingSize < 0 || _remaining.Count < DoneWhenRemainingSize)
            {
                Reply(false);
            }
        }

        protected override bool Receive(object message)
        {
            return message.Match()
                          .With<WriteAck>(x =>
                          {
                              _remaining = _remaining.Remove(Sender.Path.Address);
                              if (_remaining.Count == DoneWhenRemainingSize)
                              {
                                  Reply(true);
                              }
                          })
                          .With<SendToSecondary>(x =>
                          {
                              foreach (var n in _primaryAndSecondaryNodes.Value.Item2)
                              {
                                  Replica(n).Tell(_write);
                              }
                          })
                          .With<ReceiveTimeout>(x => Reply(false))
                          .WasHandled;
        }

        private void Reply(bool ok)
        {
            if(ok && _envelope.Data == DeletedData.Instance)
            {
                _replyTo.Tell(new DeleteSuccess<IReplicatedData>(_key), Context.Parent);
            }
            else if(ok)
            {
                _replyTo.Tell(new UpdateSuccess<IReplicatedData>(_key, _req), Context.Parent);
            }
            else if(_envelope.Data == DeletedData.Instance)
            {
                _replyTo.Tell(new ReplicationDeletedFailure<IReplicatedData>(_key), Context.Parent);
            }
            else
            {
                _replyTo.Tell(new UpdateTimeout<IReplicatedData>(_key, _req), Context.Parent);
            }
            Context.Stop(Self);
        }

        public WriteAggregator(Key<IReplicatedData> key, DataEnvelope envelope, IWriteConsistency consistency, object req, IImmutableSet<Address> nodes, IActorRef replyTo)
            : base(nodes)
        {
            _key = key;
            _envelope = envelope;
            _consistency = consistency;
            _req = req;
            _replyTo = replyTo;
            _write = new Write(key.Id, envelope);
        }
    }
}
