//-----------------------------------------------------------------------
// <copyright file="SnapshotSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Text;
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
        private Information _transportInformation;

        public SnapshotSerializer(ExtendedActorSystem system)
            : base(system)
        {
        }
        public Information TransportInformation
        {
            get
            {
                return _transportInformation ?? (_transportInformation = GetTransportInformation());
            }
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
            int headerLength;
            using (var memstream = new MemoryStream(bytes)) headerLength = ReadInt(memstream);

            var headerBytes = new ArraySegment<byte>(bytes, 4, headerLength);
            var snapshotBytes = new byte[bytes.Length - headerLength - 4];
            Array.Copy(bytes, headerLength + 4, snapshotBytes, 0, snapshotBytes.Length);

            var header = ParseSnapshotHeader(headerBytes);

            var snapshotType = header.Manifest != null
                ? Type.GetType(header.Manifest)
                : null;

            return system.Serialization.Deserialize(snapshotBytes, header.SerializerId, snapshotType);
        }

        private SnapshotHeader ParseSnapshotHeader(ArraySegment<byte> headerBytes)
        {
            using (var headerIn = new MemoryStream(headerBytes.Array, headerBytes.Offset, headerBytes.Count, false))
            {
                var serializerId = ReadInt(headerIn);
                var remainingBytesCount = headerIn.Length - headerIn.Position;
                string manifest = null;
                if (remainingBytesCount != 0L)
                {
                    var manifestBytes = new byte[remainingBytesCount];
                    headerIn.Write(manifestBytes, 0, manifestBytes.Length);
                    manifest = Encoding.UTF8.GetString(manifestBytes);
                }

                return new SnapshotHeader(serializerId, manifest);
            }
        }

        private byte[] SnapshotToBinary(object snapshot)
        {
            if (TransportInformation != null)
            {
                Akka.Serialization.Serialization.CurrentTransportInformation = TransportInformation;
            }

            var serializer = system.Serialization.FindSerializerFor(snapshot);
            using (var headerOut = new MemoryStream())
            {
                WriteInt(headerOut, serializer.Identifier);
                if (serializer.IncludeManifest)
                {
                    var snapshotTypeBinary = Encoding.UTF8.GetBytes(snapshot.GetType().FullName);
                    headerOut.Write(snapshotTypeBinary, 0, snapshotTypeBinary.Length);
                }

                var headerBinary = headerOut.ToArray();

                using (var output = new MemoryStream())
                {
                    WriteInt(output, headerBinary.Length);
                    output.Write(headerBinary, 0, headerBinary.Length);
                    var snapshotBytes = serializer.ToBinary(snapshot);
                    output.Write(snapshotBytes, 0, snapshotBytes.Length);

                    return output.ToArray();
                }
            }
        }

        private void WriteInt(MemoryStream buffer, int i)
        {
            buffer.WriteByte((byte)(i & 0xff));
            buffer.WriteByte((byte)((i >> 8) & 0xff));
            buffer.WriteByte((byte)((i >> 16) & 0xff));
            buffer.WriteByte((byte)((i >> 24) & 0xff));
        }

        private int ReadInt(MemoryStream buffer)
        {
            var i = 0;
            var j = 0;
            if ((j = buffer.ReadByte()) != -1) i |= j << 0;
            if ((j = buffer.ReadByte()) != -1) i |= j << 8;
            if ((j = buffer.ReadByte()) != -1) i |= j << 16;
            if ((j = buffer.ReadByte()) != -1) i |= j << 24;

            return i;
        }

        private Information GetTransportInformation()
        {
            var address = system.Provider.DefaultAddress;
            return !string.IsNullOrEmpty(address.Host)
                ? new Information { Address = address, System = system }
                : null;
        }
    }
}
