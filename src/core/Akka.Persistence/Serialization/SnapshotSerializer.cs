//-----------------------------------------------------------------------
// <copyright file="SnapshotSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="data">TBD</param>
        public Snapshot(object data)
        {
            Data = data;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public object Data { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        private bool Equals(Snapshot other)
        {
            return Equals(Data, other.Data);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Snapshot && Equals((Snapshot)obj);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            return (Data != null ? Data.GetHashCode() : 0);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class SnapshotHeader
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="serializerId">TBD</param>
        /// <param name="manifest">TBD</param>
        public SnapshotHeader(int serializerId, string manifest)
        {
            SerializerId = serializerId;
            Manifest = manifest;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public int SerializerId { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public string Manifest { get; private set; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class SnapshotSerializer : Serializer
    {
        private Information _transportInformation;

        /// <summary>
        /// Initializes a new instance of the <see cref="SnapshotSerializer"/> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer.</param>
        public SnapshotSerializer(ExtendedActorSystem system)
            : base(system)
        {
        }
        /// <summary>
        /// TBD
        /// </summary>
        public Information TransportInformation
        {
            get
            {
                return _transportInformation ?? (_transportInformation = GetTransportInformation());
            }
        }

        /// <summary>
        /// Returns whether this serializer needs a manifest in the fromBinary method
        /// </summary>
        public override bool IncludeManifest
        {
            get { return false; }
        }

        /// <summary>
        /// Serializes the given object into a byte array
        /// </summary>
        /// <param name="obj">The object to serialize</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the <see cref="SnapshotSerializer"/> cannot serialize the specified <paramref name="obj"/>.
        /// The specified <paramref name="obj" /> must be of type <see cref="Snapshot"/>.
        /// </exception>
        /// <returns>
        /// A byte array containing the serialized object
        /// </returns>
        public override byte[] ToBinary(object obj)
        {
            if (obj is Snapshot) return SnapshotToBinary((obj as Snapshot).Data);

            throw new ArgumentException($"{typeof(SnapshotSerializer)} cannot serialize object of type {obj.GetType()}", nameof(obj));
        }

        /// <summary>
        /// Deserializes a byte array into an object of type <paramref name="type" />.
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="type">The type of object contained in the array</param>
        /// <returns>
        /// The object contained in the array
        /// </returns>
        public override object FromBinary(byte[] bytes, Type type)
        {
            return new Snapshot(SnapshotFromBinary(bytes));
        }

        private byte[] SnapshotToBinary(object snapshot)
        {

            return TransportInformation != null
                ? Akka.Serialization.Serialization.SerializeWithTransport(TransportInformation.System,
                    TransportInformation.Address, () => Serialize(snapshot))
                : Serialize(snapshot);
        }

        private byte[] Serialize(object snapshot)
        {
            var serializer = system.Serialization.FindSerializerFor(snapshot);
            byte[] headerBytes;
            using (var headerOut = new MemoryStream())
            {
                WriteInt(headerOut, serializer.Identifier);
                var serializerManifest = serializer as SerializerWithStringManifest;
                if (serializerManifest != null)
                {
                    var manifest = serializerManifest.Manifest(snapshot);
                    if (!string.IsNullOrEmpty(manifest))
                    {
                        var manifestBinary = Encoding.UTF8.GetBytes(manifest);
                        headerOut.Write(manifestBinary, 0, manifestBinary.Length);
                    }
                }
                else if (serializer.IncludeManifest)
                {
                    var snapshotTypeBinary = Encoding.UTF8.GetBytes(TypeQualifiedNameForManifest(snapshot.GetType()));
                    headerOut.Write(snapshotTypeBinary, 0, snapshotTypeBinary.Length);
                }

                headerBytes = headerOut.ToArray();
            }

            using (var output = new MemoryStream())
            {
                WriteInt(output, headerBytes.Length);
                output.Write(headerBytes, 0, headerBytes.Length);

                var snapshotBytes = serializer.ToBinary(snapshot);
                output.Write(snapshotBytes, 0, snapshotBytes.Length);

                return output.ToArray();
            }
        }

        private object SnapshotFromBinary(byte[] bytes)
        {
            int headerLength;
            using (var memstream = new MemoryStream(bytes)) headerLength = ReadInt(memstream);

            var headerBytes = new ArraySegment<byte>(bytes, 4, headerLength);
            var snapshotBytes = new byte[bytes.Length - headerLength - 4];
            Array.Copy(bytes, headerLength + 4, snapshotBytes, 0, snapshotBytes.Length);

            var header = ParseSnapshotHeader(headerBytes);
            var manifest = header.Manifest != null ? header.Manifest : string.Empty;

            return system.Serialization.Deserialize(snapshotBytes, header.SerializerId, manifest);
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
                    headerIn.Read(manifestBytes, 0, manifestBytes.Length);
                    manifest = Encoding.UTF8.GetString(manifestBytes);
                }

                return new SnapshotHeader(serializerId, manifest);
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
