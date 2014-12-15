using System;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.Persistence.Serialization
{
    public struct Snapshot
    {
        public Snapshot(object data) : this()
        {
            Data = data;
        }

        public object Data { get; private set; }
    }

    public struct SnapshotHeader
    {
        public SnapshotHeader(int serializerId, string manifest) : this()
        {
            SerializerId = serializerId;
            Manifest = manifest;
        }

        public int SerializerId { get; private set; }
        public string Manifest { get; private set; }
    }

    public class SnapshotSerializer : Serializer
    {
        public SnapshotSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier
        {
            get { return 8; }
        }

        public override bool IncludeManifest
        {
            get { return false; }
        }

        public override byte[] ToBinary(object obj)
        {
            throw new NotImplementedException();
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            throw new NotImplementedException();
        }
    }
}