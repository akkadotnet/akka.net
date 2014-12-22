using System;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.Persistence.Serialization
{
    /// <summary>
    /// Wrapper for snapshot data.
    /// </summary>
    public sealed class Snapshot
    {
        public Snapshot(object data)
        {
            Data = data;
        }

        public object Data { get; private set; }
    }

    public sealed class SnapshotHeader
    {
        public SnapshotHeader(int serializerId, string manifest)
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
            if (obj is Snapshot) return SnapshotToBinary((obj as Snapshot).Data);

            throw new ArgumentException(typeof(SnapshotSerializer) + "cannot serialize object of type " + obj.GetType(), "obj");
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            return new Snapshot(SnapshotFromBinary(bytes));
        }

        private object SnapshotFromBinary(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private byte[] SnapshotToBinary(object data)
        {
            throw new NotImplementedException();
        }
    }
}