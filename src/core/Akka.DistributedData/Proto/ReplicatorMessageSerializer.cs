using Akka.Actor;
using Akka.Cluster;
using Akka.DistributedData.Internal;
using Akka.IO;
using Akka.Serialization;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using dm = Akka.DistributedData.Messages;

namespace Akka.DistributedData.Proto
{
    public interface IReplicatorMessageSerializer
    { }

    public class ReplicatorMessageSerializer : SerializerWithStringManifest, ISerializationSupport
    {
        private const string GetManifest = "A";
        private const string GetSuccessManifest = "B";
        private const string NotFoundManifest = "C";
        private const string GetFailureManifest = "D";
        private const string SubscribeManifest = "E";
        private const string UnsubscribeManifest = "F";
        private const string ChangedManifest = "G";
        private const string DataEnvelopeManifest = "H";
        private const string WriteManifest = "I";
        private const string WriteAckManifest = "J";
        private const string ReadManifest = "K";
        private const string ReadResultManifest = "L";
        private const string StatusManifest = "M";
        private const string GossipManifest = "N";

        readonly ExtendedActorSystem _system;

        public ReplicatorMessageSerializer(ExtendedActorSystem system)
            : base(system)
        {
            _system = system;
        }

        public override string Manifest(object obj)
        {
            if (obj is DataEnvelope) { return DataEnvelopeManifest; }
            else if (obj is Write) { return WriteManifest; }
            else if (obj is WriteAck) { return WriteAckManifest; }
            else if (obj is Read) { return ReadManifest; }
            else if (obj is ReadResult) { return ReadResultManifest; }
            else if (obj is Akka.DistributedData.Internal.Status) { return StatusManifest; }
            else if (obj is Gossip) { return GossipManifest; }
            else if (obj is IGet) { return GetManifest; }
            else if (obj is IGetSuccess) { return GetSuccessManifest; }
            else if (obj is IChanged) { return ChangedManifest; }
            else if (obj is INotFound) { return NotFoundManifest; }
            else if (obj is IGetFailure) { return GetFailureManifest; }
            else if (obj is ISubscribe) { return SubscribeManifest; }
            else if (obj is IUnsubscribe) { return UnsubscribeManifest; }
            else { throw new ArgumentException("Unable to serialize {0}", obj.GetType().Name); }
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            switch(manifest)
            {
                case GetManifest:
                    return GetFromBinary(bytes);
                case GetSuccessManifest:
                    return GetSuccessFromBinary(bytes);
                case NotFoundManifest:
                    return NotFoundFromBinary(bytes);
                case GetFailureManifest:
                    return GetFailureFromBinary(bytes);
                case SubscribeManifest:
                    return SubscribeFromBinary(bytes);
                case UnsubscribeManifest:
                    return UnsubscribeFromBinary(bytes);
                case ChangedManifest:
                    return ChangedFromBinary(bytes);
                case DataEnvelopeManifest:
                    return DataEnvelopeFromBinary(bytes);
                case WriteManifest:
                    return WriteFromBinary(bytes);
                case WriteAckManifest:
                    return WriteAck.Instance;
                case ReadManifest:
                    return ReadFromBinary(bytes);
                case ReadResultManifest:
                    return ReadResultFromBinary(bytes);
                case StatusManifest:
                    return StatusFromBinary(bytes);
                case GossipManifest:
                    return GossipFromBinary(bytes);
                default:
                    throw new ArgumentException(String.Format("Unable to serialize type {0}", manifest));
            }
        }

        public override byte[] ToBinary(object obj)
        {
            if (obj is DataEnvelope) { return DataEnvelopeToProto((DataEnvelope)obj).ToByteArray(); }
            else if (obj is Write) { return WriteToProto((Write)obj).ToByteArray(); }
            else if (obj is WriteAck) { return dm.Empty.DefaultInstance.ToByteArray(); }
            else if (obj is Read) { return ReadToProto((Read)obj).ToByteArray(); }
            else if (obj is ReadResult) { return ReadResultToProto((ReadResult)obj).ToByteArray(); }
            else if (obj is Akka.DistributedData.Internal.Status) { return StatusToProto((Akka.DistributedData.Internal.Status)obj).ToByteArray(); }
            else if (obj is Gossip) { return GossipToProto((Gossip)obj).ToByteArray(); }
            else if (obj is IGet) { return GetToProto((IGet)obj).ToByteArray(); }
            else if (obj is IGetSuccess) { return GetSuccessToProto((IGetSuccess)obj).ToByteArray(); }
            else if (obj is IChanged) { return ChangedToproto((IChanged)obj).ToByteArray(); }
            else if (obj is INotFound) { return NotFoundToProto((INotFound)obj).ToByteArray(); }
            else if (obj is IGetFailure) { return GetFailureToProto((IGetFailure)obj).ToByteArray(); }
            else if (obj is ISubscribe) { return SubscribeToProto((ISubscribe)obj).ToByteArray(); }
            else if (obj is IUnsubscribe) { return UnsubscribeToProto((IUnsubscribe)obj).ToByteArray(); }
            else { throw new ArgumentException("Unable to serialize {0}", obj.GetType().Name); }
        }

        public override int Identifier
        {
            get { return 12; }
        }

        public Actor.ExtendedActorSystem System
        {
            get { return _system; }
        }

        private Serialization.Serialization _ser;
        public Serialization.Serialization Serialization
        {
            get
            {
                if (_ser == null) _ser = System.Serialization;
                return _ser;
            }
        }

        string _addressProtocol;
        public string AddressProtocol
        {
            get
            {
                if (_addressProtocol == null) _addressProtocol = System.Provider.DefaultAddress.Protocol;
                return _addressProtocol;
            }
        }

        private dm.Status StatusToProto(Internal.Status status)
        {
            var b = dm.Status.CreateBuilder()
                             .SetChunk((uint)status.Chunk)
                             .SetTotChunks((uint)status.TotChunks);
            foreach(var kvp in status.Digests)
            {
                var key = kvp.Key;
                var digest = kvp.Value;
                b.AddEntries(dm.Status.Types.Entry.CreateBuilder()
                                .SetKey(key)
                                .SetDigest(Google.ProtocolBuffers.ByteString.CopyFrom(digest.ToArray())));
            }
            return b.Build();
        }

        private Akka.DistributedData.Internal.Status StatusFromBinary(byte[] bytes)
        {
            var status = dm.Status.ParseFrom(bytes);
            var entries = status.EntriesList.Select(x =>
                {
                    var key = x.Key;
                    var digest = ByteString.Create(x.Digest.ToByteArray());
                    return new KeyValuePair<string, ByteString>(key, digest);
                }).ToImmutableDictionary();
            return new Internal.Status(entries, (int)status.Chunk, (int)status.TotChunks);
        }

        private dm.Gossip GossipToProto(Gossip gossip)
        {
            var b = dm.Gossip.CreateBuilder().SetSendBack(gossip.SendBack);
            foreach(var g in gossip.UpdatedData)
            {
                var key = g.Key;
                var data = g.Value;
                b.AddEntries(dm.Gossip.Types.Entry.CreateBuilder()
                               .SetKey(key)
                               .SetEnvelope(DataEnvelopeToProto(data)));
            }
            return b.Build();
        }

        private Gossip GossipFromBinary(byte[] bytes)
        {
            var gossip = dm.Gossip.ParseFrom(bytes);
            var entries = gossip.EntriesList.Select(x =>
                {
                    var key = x.Key;
                    var env = DataEnvelopeFromProto(x.Envelope) as DataEnvelope;
                    return new KeyValuePair<string, DataEnvelope>(key, env);
                }).ToImmutableDictionary();
            return new Gossip(entries, gossip.SendBack);
        }

        private dm.Get GetToProto(IGet get)
        {
            int consistencyValue = 0;
            if (get.Consistency is ReadLocal) { consistencyValue = 1; }
            else if (get.Consistency is ReadAll) { consistencyValue = -1; }
            else if (get.Consistency is ReadMajority) { consistencyValue = 0; }
            else { consistencyValue = ((ReadFrom)get.Consistency).N; }
            var b = dm.Get.CreateBuilder()
                          .SetKey(this.OtherMessageToProto(get.Key))
                          .SetConsistency(consistencyValue)
                          .SetTimeout((uint)get.Consistency.Timeout.TotalMilliseconds);
            if(get.Request != null)
            {
                b.SetRequest(this.OtherMessageToProto(get.Request));
            }
            return b.Build();
        }

        private object GetFromBinary(byte[] bytes)
        {
            var get = dm.Get.ParseFrom(bytes);
            var key = this.OtherMessageFromProto(get.Key);
            var request = get.HasRequest ? this.OtherMessageFromProto(get.Request) : null;
            var timeout = TimeSpan.FromMilliseconds(get.Timeout);
            IReadConsistency consistency;
            if (get.Consistency == 0) { consistency = new ReadMajority(timeout); }
            else if (get.Consistency == -1) { consistency = new ReadAll(timeout); }
            else if (get.Consistency == 1) { consistency = ReadLocal.Instance; }
            else { consistency = new ReadFrom(get.Consistency, timeout); }
            var keyInterfaceType = key.GetType().GetInterface("IKey`1").GetGenericArguments()[0];
            var invokeType = typeof(Get<>).MakeGenericType(keyInterfaceType);
            return Activator.CreateInstance(invokeType, key, consistency, request);
        }

        private dm.GetSuccess GetSuccessToProto(IGetSuccess succ)
        {
            var b = dm.GetSuccess.CreateBuilder()
                                 .SetKey(this.OtherMessageToProto(succ.Key))
                                 .SetData(this.OtherMessageToProto(succ.Data));
            if(succ.Request != null)
            {
                b.SetRequest(this.OtherMessageToProto(succ.Request));
            }
            return b.Build();
        }

        private object GetSuccessFromBinary(byte[] bytes)
        {
            var succ = dm.GetSuccess.ParseFrom(bytes);
            var key = this.OtherMessageFromProto(succ.Key) as IKey;
            var request = succ.HasRequest ? this.OtherMessageFromProto(succ.Request) : null;
            var data = this.OtherMessageFromProto(succ.Data) as IReplicatedData;
            var keyInterfaceType = key.GetType().GetInterface("IKey`1").GetGenericArguments()[0];
            var invokeType = typeof(GetSuccess<>).MakeGenericType(keyInterfaceType);
            return Activator.CreateInstance(invokeType, new object[] { key, request, data });
        }

        private dm.NotFound NotFoundToProto(INotFound notFound)
        {
            var b = dm.NotFound.CreateBuilder()
                               .SetKey(this.OtherMessageToProto(notFound.Key));
            if(notFound.Request != null)
            {
                b.SetRequest(this.OtherMessageToProto(notFound.Request));
            }
            return b.Build();
        }

        private object NotFoundFromBinary(byte[] bytes)
        {
            var nf = dm.NotFound.ParseFrom(bytes);
            var request = nf.HasRequest ? this.OtherMessageFromProto(nf.Request) : null;
            var key = this.OtherMessageFromProto(nf.Key);
            var keyInterfaceType = key.GetType().GetInterface("IKey`1").GetGenericArguments()[0];
            var invokeType = typeof(NotFound<>).MakeGenericType(keyInterfaceType);
            return Activator.CreateInstance(invokeType, new object[] { key, request });
        }

        private dm.GetFailure GetFailureToProto(IGetFailure fail)
        {
            var b = dm.GetFailure.CreateBuilder()
                                 .SetKey(this.OtherMessageToProto(fail.Key));
            if(fail.Request != null)
            {
                b.SetRequest(this.OtherMessageToProto(fail.Request));
            }
            return b.Build();
        }

        private object GetFailureFromBinary(byte[] bytes)
        {
            var fail = dm.GetFailure.ParseFrom(bytes);
            var req = fail.HasRequest ? this.OtherMessageFromProto(fail.Request) : null;
            var key = this.OtherMessageFromProto(fail.Key) as IKey;
            var keyInterfaceType = key.GetType().GetInterface("IKey`1").GetGenericArguments()[0];
            var invokeType = typeof(GetFailure<>).MakeGenericType(keyInterfaceType);
            return Activator.CreateInstance(invokeType, new object[] { key, req });
        }

        private dm.Subscribe SubscribeToProto(ISubscribe sub)
        {
            var path = Akka.Serialization.Serialization.SerializedActorPath(sub.Subscriber);
            return dm.Subscribe.CreateBuilder()
                               .SetKey(this.OtherMessageToProto(sub.Key))
                               .SetRef(path)
                               .Build();
        }

        private object SubscribeFromBinary(byte[] bytes)
        {
            var sub = dm.Subscribe.ParseFrom(bytes);
            var key = this.OtherMessageFromProto(sub.Key) as IKey;
            var actorRef = this.ResolveActorRef(sub.Ref);
            var keyInterfaceType = key.GetType().GetInterface("IKey`1").GetGenericArguments()[0];
            var invokeType = typeof(Subscribe<>).MakeGenericType(keyInterfaceType);
            return Activator.CreateInstance(invokeType, new object[] { key, actorRef });
        }

        private dm.Unsubscribe UnsubscribeToProto(IUnsubscribe data)
        {
            return dm.Unsubscribe.CreateBuilder()
                                 .SetKey(this.OtherMessageToProto(data.Key))
                                 .SetRef(Akka.Serialization.Serialization.SerializedActorPath(data.Subscriber))
                                 .Build();
        }

        private object UnsubscribeFromBinary(byte[] bytes)
        {
            var unsub = dm.Unsubscribe.ParseFrom(bytes);
            var key = this.OtherMessageFromProto(unsub.Key) as IKey;
            var actorRef = this.ResolveActorRef(unsub.Ref);
            var keyInterfaceType = key.GetType().GetInterface("IKey`1").GetGenericArguments()[0];
            var invokeType = typeof(Unsubscribe<>).MakeGenericType(keyInterfaceType);
            return Activator.CreateInstance(invokeType, new object[] { key, actorRef });
        }

        private dm.Changed ChangedToproto(IChanged data)
        {
            return dm.Changed.CreateBuilder()
                             .SetKey(this.OtherMessageToProto(data.Key))
                             .SetData(this.OtherMessageToProto(data.Data))
                             .Build();
        }

        private object ChangedFromBinary(byte[] bytes)
        {
            var changed = dm.Changed.ParseFrom(bytes);
            var data = this.OtherMessageFromProto(changed.Data) as IReplicatedData;
            var key = this.OtherMessageFromProto(changed.Key) as IKey;
            var keyInterfaceType = key.GetType().GetInterface("IKey`1").GetGenericArguments()[0];
            var invokeType = typeof(Changed<>).MakeGenericType(keyInterfaceType);
            return Activator.CreateInstance(invokeType, new object[] { key, data });
        }

        private dm.DataEnvelope DataEnvelopeToProto(DataEnvelope envelope)
        {
            var dataEnvelopeBuilder = dm.DataEnvelope.CreateBuilder()
                                                     .SetData(this.OtherMessageToProto(envelope.Data));
            foreach (var pruning in envelope.Pruning)
            {
                var removedAddress = pruning.Key;
                var state = pruning.Value;
                var b = dm.DataEnvelope.Types.PruningEntry.CreateBuilder()
                                                          .SetRemovedAddress(this.UniqueAddressToProto(removedAddress))
                                                          .SetOwnerAddress(this.UniqueAddressToProto(state.Owner));
                var phase = state.Phase as PruningInitialized;
                if (phase != null)
                {
                    var seen = phase.Seen.OrderBy(x => x, Member.AddressOrdering).Select(this.AddressToProto);
                    foreach (var x in seen)
                    {
                        b.AddSeen(x);
                    }
                    b.SetPerformed(false);
                }
                else
                {
                    b.SetPerformed(true);
                }
                dataEnvelopeBuilder.AddPruning(b);
            }
            return dataEnvelopeBuilder.Build();
        }

        private DataEnvelope DataEnvelopeFromBinary(byte[] bytes)
        {
            return DataEnvelopeFromProto(dm.DataEnvelope.ParseFrom(bytes));
        }

        private DataEnvelope DataEnvelopeFromProto(dm.DataEnvelope envelope)
        {
            var pruning = envelope.PruningList.Select(x =>
                {
                    IPruningPhase phase = x.Performed ? (IPruningPhase)PruningPerformed.Instance : new PruningInitialized(x.SeenList.Select(this.AddressFromProto).ToImmutableHashSet());
                    var state = new PruningState(this.UniqueAddressFromProto(x.OwnerAddress), phase);
                    var removed = this.UniqueAddressFromProto(x.RemovedAddress);
                    return new KeyValuePair<UniqueAddress, PruningState>(removed, state);
                }).ToImmutableDictionary();
            var data = this.OtherMessageFromProto(envelope.Data) as IReplicatedData;
            return new DataEnvelope(data, pruning);
        }

        private dm.Write WriteToProto(Write write)
        {
            return dm.Write.CreateBuilder()
                           .SetKey(write.Key)
                           .SetEnvelope(DataEnvelopeToProto(write.Envelope))
                           .Build();
        }

        private Write WriteFromBinary(byte[] bytes)
        {
            var write = dm.Write.ParseFrom(bytes);
            return new Write(write.Key, DataEnvelopeFromProto(write.Envelope));
        }

        private dm.Read ReadToProto(Read read)
        {
            return dm.Read.CreateBuilder().SetKey(read.Key).Build();
        }

        private Read ReadFromBinary(byte[] bytes)
        {
            return new Read(dm.Read.ParseFrom(bytes).Key);
        }

        private dm.ReadResult ReadResultToProto(ReadResult readResult)
        {
            var b = dm.ReadResult.CreateBuilder();
            if(readResult.Envelope != null)
            {
                b.SetEnvelope(DataEnvelopeToProto(readResult.Envelope));
            }
            return b.Build();
        }

        private ReadResult ReadResultFromBinary(byte[] bytes)
        {
            var readResult = dm.ReadResult.ParseFrom(bytes);
            var envelope = readResult.HasEnvelope ? DataEnvelopeFromProto(readResult.Envelope) : null;
            return new ReadResult(envelope);
        }
    }
}
