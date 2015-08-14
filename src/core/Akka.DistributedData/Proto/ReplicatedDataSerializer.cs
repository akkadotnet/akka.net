using Akka.Actor;
using Akka.Cluster;
using Akka.Serialization;
using Google.ProtocolBuffers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using rd = Akka.DistributedData.Messages;
using System.Collections.Immutable;

namespace Akka.DistributedData.Proto
{
    public interface IReplicatedDataSerialization
    { }

    public class ReplicatedDataSerializer : SerializerWithStringManifest, ISerializationSupport
    {
        const int BufferSize = 1024 * 4;

        const string DeletedDataManifest = "A";
        const string GSetManifest = "B";
        const string GSetKeyManifest = "b";
        const string ORSetManifest = "C";
        const string ORSetKeyManifest = "c";
        const string FlagManifest = "D";
        const string FlagKeyManifest = "d";
        const string LWWRegisterManifest = "E";
        const string LWWRegisterKeyManifest = "e";
        const string GCounterManifest = "F";
        const string GCounterKeyManifest = "f";
        const string PNCounterManifest = "G";
        const string PNCounterKeyManifest = "g";
        const string ORMapManifest = "H";
        const string ORMapKeyManifest = "h";
        const string LWWMapManifest = "I";
        const string LWWMapKeyManifest = "i";
        const string PNCounterMapManifest = "J";
        const string PNCounterMapKeyManifest = "j";
        const string ORMultiMapManifest = "K";
        const string ORMultiMapKeyManifest = "k";
        const string VersionVectorManifest = "L";

        readonly ExtendedActorSystem _system;

        public override string Manifest(object o)
        {
            if (o is DeletedData) { return DeletedDataManifest; }
            else if (o is Flag) { return FlagManifest; }
            else if (o is GCounter) { return GCounterManifest; }

            else if (o is FlagKey) { return FlagKeyManifest; }
            else if (o is GCounterKey) { return GCounterKeyManifest; }

            else { throw new ArgumentException("Unsupported object type to be serialized"); }
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            switch(manifest)
            {
                case DeletedDataManifest:
                    return DeletedData.Instance;
                case FlagManifest:
                    return FlagFromBinary(bytes);
                case FlagKeyManifest:
                    return new FlagKey(KeyIdFromBinary(bytes));
                case GCounterManifest:
                    return GCounterFromBinary(bytes);
                case GCounterKeyManifest:
                    return new GCounterKey(KeyIdFromBinary(bytes));
                default:
                    throw new ArgumentException(String.Format("Can't serialize manifest {0}", manifest));
            }
        }

        public override int Identifier
        {
            get { return System.Settings.Config.GetInt("akka.actor.serialization-identifiers.ReplicatedDataSerializer"); }
        }

        public override byte[] ToBinary(object obj)
        {
            if (obj is Flag) { return FlagToProto((Flag)obj).ToByteArray(); }
            else if (obj is GCounter) { return GCounterToProto((GCounter)obj).ToByteArray(); }
            else if (obj is DeletedData) { return rd.Empty.DefaultInstance.ToByteArray(); }
            else if (obj is Key<IReplicatedData>) { return KeyIdToBinary(((Key<IReplicatedData>)obj).Id); }
            else { throw new ArgumentException(String.Format("Can't serialize object of type {0}", obj.GetType().Name)); }
        }

        public ExtendedActorSystem System
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

        private string _addressProtocol;
        public string AddressProtocol
        {
            get
            {
                if (_addressProtocol == null) _addressProtocol = System.Provider.DefaultAddress.Protocol;
                return _addressProtocol;
            }
        }

        private Flag FlagFromBinary(byte[] bytes)
        {
            return FlagFromProto(rd.Flag.ParseFrom(bytes));
        }

        private Flag FlagFromProto(rd.Flag flag)
        {
            return new Flag(flag.Enabled);
        }

        private rd.Flag FlagToProto(Flag flag)
        {
            return rd.Flag.CreateBuilder().SetEnabled(flag.Enabled).Build();
        }

        private GCounter GCounterFromBinary(byte[] bytes)
        {
            return GCounterFromProto(rd.GCounter.ParseFrom(bytes));
        }

        private GCounter GCounterFromProto(rd.GCounter gcounter)
        {
            var entries = gcounter.EntriesList.Select(x => new KeyValuePair<UniqueAddress, BigInteger>(this.UniqueAddressFromProto(x.Node), new BigInteger(x.Value.ToByteArray()))).ToImmutableDictionary();
            return new GCounter(entries);
        }

        private rd.GCounter GCounterToProto(GCounter gcounter)
        {
            var b = rd.GCounter.CreateBuilder();
            foreach(var kvp in gcounter.State.OrderBy(x => x.Key))
            {
                b.AddEntries(rd.GCounter.Types.Entry.CreateBuilder()
                                                    .SetNode(this.UniqueAddressToProto(kvp.Key))
                                                    .SetValue(ByteString.CopyFrom(kvp.Value.ToByteArray())));
            }
            return b.Build();
        }

        private byte[] KeyIdToBinary(string id)
        {
            return Encoding.UTF8.GetBytes(id);
        }

        private string KeyIdFromBinary(byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        public ReplicatedDataSerializer(ExtendedActorSystem system)
            : base(system)
        {
            _system = system;
        }
    }
}
