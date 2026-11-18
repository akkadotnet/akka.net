//-----------------------------------------------------------------------
// <copyright file="PrimitiveSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using Akka.Util;

namespace Akka.Remote.Serialization
{
    /// <summary>
    /// Serializes <see cref="string"/>, <see cref="int"/> and <see cref="long"/> under serializer id 17.
    /// </summary>
    /// <remarks>
    /// The wire format is raw UTF-8 for strings and little-endian two's complement for <see cref="int"/> and
    /// <see cref="long"/>. It matches the bytes earlier releases wrote. This is a native <see cref="SerializerV2"/>:
    /// it writes straight into the caller's <see cref="IBufferWriter{T}"/> and reads straight from a
    /// <see cref="ReadOnlySequence{T}"/>, so Artery does not need the V1 adapter to reach it.
    /// </remarks>
    public sealed class PrimitiveSerializers : SerializerV2
    {
        internal const string StringManifest = "S";
        internal const string Int32Manifest = "I";
        internal const string Int64Manifest = "L";

        // .Net Core manifests
        internal const string StringManifestNetCore = "System.String, System.Private.CoreLib";
        internal const string Int32ManifestNetCore = "System.Int32, System.Private.CoreLib";
        internal const string Int64ManifestNetCore = "System.Int64, System.Private.CoreLib";

        // .Net Framework manifests
        internal const string StringManifestNetFx = "System.String, mscorlib";
        internal const string Int32ManifestNetFx = "System.Int32, mscorlib";
        internal const string Int64ManifestNetFx = "System.Int64, mscorlib";

        private const int Int32Size = sizeof(int);
        private const int Int64Size = sizeof(long);

        private readonly bool _useLegacyBehavior;

        /// <summary>
        /// Initializes a new instance of the <see cref="PrimitiveSerializers" /> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer. </param>
        /// <param name="config">Config object containing the serializer settings</param>
        public PrimitiveSerializers(ExtendedActorSystem system, Config config) : base(system)
        {
            if (config == null)
                throw new ConfigurationException("configuration is null");

            _useLegacyBehavior = config.GetBoolean("use-legacy-behavior");
        }

        /// <inheritdoc />
        public override int Identifier => 17;

        /// <inheritdoc />
        public override string Manifest(object obj)
        {
            if (_useLegacyBehavior)
                return obj.GetType().TypeQualifiedName();

            switch (obj)
            {
                case string _:
                    return StringManifest;
                case int _:
                    return Int32Manifest;
                case long _:
                    return Int64Manifest;
                default:
                    throw new ArgumentException($"Cannot serialize object of type [{obj.GetType()}] in [{GetType()}]");
            }
        }

        /// <inheritdoc />
        public override int SizeHint(object obj)
        {
            switch (obj)
            {
                case string s:
                    return Encoding.UTF8.GetByteCount(s);
                case int _:
                    return Int32Size;
                case long _:
                    return Int64Size;
                default:
                    throw CannotSerialize(obj);
            }
        }

        /// <inheritdoc />
        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            switch (obj)
            {
                case string s:
                    {
                        var byteCount = Encoding.UTF8.GetByteCount(s);
                        if (byteCount == 0)
                            return 0;

                        var span = writer.GetSpan(byteCount);
                        var written = Encoding.UTF8.GetBytes(s, span);
                        writer.Advance(written);
                        return written;
                    }
                case int i:
                    // BitConverter used the machine byte order; every supported .NET platform is little-endian,
                    // so writing little-endian here produces the same bytes as before.
                    BinaryPrimitives.WriteInt32LittleEndian(writer.GetSpan(Int32Size), i);
                    writer.Advance(Int32Size);
                    return Int32Size;
                case long l:
                    BinaryPrimitives.WriteInt64LittleEndian(writer.GetSpan(Int64Size), l);
                    writer.Advance(Int64Size);
                    return Int64Size;
                default:
                    throw CannotSerialize(obj);
            }
        }

        /// <inheritdoc />
        public override byte[] ToBinary(object obj)
        {
            // The base implementation goes through an ArrayBufferWriter and copies. These are the cheapest
            // byte[] paths: one exact-size allocation, no copy.
            switch (obj)
            {
                case string s:
                    return Encoding.UTF8.GetBytes(s);
                case int i:
                    {
                        var bytes = new byte[Int32Size];
                        BinaryPrimitives.WriteInt32LittleEndian(bytes, i);
                        return bytes;
                    }
                case long l:
                    {
                        var bytes = new byte[Int64Size];
                        BinaryPrimitives.WriteInt64LittleEndian(bytes, l);
                        return bytes;
                    }
                default:
                    throw CannotSerialize(obj);
            }
        }

        /// <inheritdoc />
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
        {
            switch (manifest)
            {
                case StringManifest:
                case StringManifestNetCore:
                case StringManifestNetFx:
                    return ReadString(bytes);
                case Int32Manifest:
                case Int32ManifestNetCore:
                case Int32ManifestNetFx:
                    return ReadInt32(bytes);
                case Int64Manifest:
                case Int64ManifestNetCore:
                case Int64ManifestNetFx:
                    return ReadInt64(bytes);
                default:
                    throw new ArgumentException($"Unimplemented deserialization of message with manifest [{manifest}] in [${GetType()}]");
            }
        }

        private static string ReadString(in ReadOnlySequence<byte> bytes)
        {
            return bytes.IsSingleSegment
                ? Encoding.UTF8.GetString(bytes.FirstSpan)
                : Encoding.UTF8.GetString(in bytes);
        }

        private static int ReadInt32(in ReadOnlySequence<byte> bytes)
        {
            var first = bytes.FirstSpan;
            if (first.Length >= Int32Size)
                return BinaryPrimitives.ReadInt32LittleEndian(first);

            ThrowIfTooShort(bytes, Int32Size);
            Span<byte> buffer = stackalloc byte[Int32Size];
            bytes.Slice(0, Int32Size).CopyTo(buffer);
            return BinaryPrimitives.ReadInt32LittleEndian(buffer);
        }

        private static long ReadInt64(in ReadOnlySequence<byte> bytes)
        {
            var first = bytes.FirstSpan;
            if (first.Length >= Int64Size)
                return BinaryPrimitives.ReadInt64LittleEndian(first);

            ThrowIfTooShort(bytes, Int64Size);
            Span<byte> buffer = stackalloc byte[Int64Size];
            bytes.Slice(0, Int64Size).CopyTo(buffer);
            return BinaryPrimitives.ReadInt64LittleEndian(buffer);
        }

        private static void ThrowIfTooShort(in ReadOnlySequence<byte> bytes, int required)
        {
            if (bytes.Length < required)
                throw new ArgumentException(
                    $"Cannot deserialize a {required}-byte primitive from [{bytes.Length}] bytes", nameof(bytes));
        }

        private static ArgumentException CannotSerialize(object obj) =>
            new ArgumentException($"Cannot serialize object of type [{obj.GetType()}]");
    }
}
