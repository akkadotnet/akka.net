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
    /// A <see cref="SerializerV2"/> covering <see cref="string"/>, <see cref="int"/>, and
    /// <see cref="long"/> primitives. Wire format is byte-identical to the legacy V1
    /// implementation: UTF-8 bytes for strings, little-endian fixed-width for integers.
    /// Serializer ID is 17.
    /// </summary>
    public sealed class PrimitiveSerializers : SerializerV2
    {
        internal const string StringManifest = "S";
        internal const string Int32Manifest = "I";
        internal const string Int64Manifest = "L";

        // .NET Core / modern runtime manifests (legacy interop)
        internal const string StringManifestNetCore = "System.String, System.Private.CoreLib";
        internal const string Int32ManifestNetCore = "System.Int32, System.Private.CoreLib";
        internal const string Int64ManifestNetCore = "System.Int64, System.Private.CoreLib";

        // .NET Framework manifests (legacy interop)
        internal const string StringManifestNetFx = "System.String, mscorlib";
        internal const string Int32ManifestNetFx = "System.Int32, mscorlib";
        internal const string Int64ManifestNetFx = "System.Int64, mscorlib";

        private readonly bool _useLegacyBehavior;

        /// <summary>
        /// Initializes a new instance of the <see cref="PrimitiveSerializers" /> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer.</param>
        /// <param name="config">Config object containing the serializer settings.</param>
        public PrimitiveSerializers(ExtendedActorSystem system, Config config) : base(system)
        {
            if (config == null)
                throw new ConfigurationException("configuration is null");

            _useLegacyBehavior = config.GetBoolean("use-legacy-behavior");
        }

        /// <inheritdoc/>
        public override string Manifest(object o)
        {
            if (_useLegacyBehavior)
                return o.GetType().TypeQualifiedName();

            return o switch
            {
                string _ => StringManifest,
                int _ => Int32Manifest,
                long _ => Int64Manifest,
                _ => throw new ArgumentException($"Cannot serialize object of type [{o.GetType()}] in [{GetType()}]")
            };
        }

        /// <inheritdoc/>
        public override int SizeHint(object o) => o switch
        {
            string s => Encoding.UTF8.GetMaxByteCount(s.Length),
            int _ => sizeof(int),
            long _ => sizeof(long),
            _ => 0
        };

        /// <inheritdoc/>
        public override void Serialize(IBufferWriter<byte> buffer, object obj)
        {
            switch (obj)
            {
                case string s:
                {
                    // Encode UTF-8 directly into the writer's span — avoids the byte[]
                    // allocation that Encoding.UTF8.GetBytes(string) requires.
                    var maxBytes = Encoding.UTF8.GetMaxByteCount(s.Length);
                    var span = buffer.GetSpan(maxBytes);
                    var written = Encoding.UTF8.GetBytes(s.AsSpan(), span);
                    buffer.Advance(written);
                    return;
                }
                case int i:
                {
                    var span = buffer.GetSpan(sizeof(int));
                    BinaryPrimitives.WriteInt32LittleEndian(span, i);
                    buffer.Advance(sizeof(int));
                    return;
                }
                case long l:
                {
                    var span = buffer.GetSpan(sizeof(long));
                    BinaryPrimitives.WriteInt64LittleEndian(span, l);
                    buffer.Advance(sizeof(long));
                    return;
                }
                default:
                    throw new ArgumentException($"Cannot serialize object of type [{obj.GetType()}]");
            }
        }

        /// <inheritdoc/>
        public override object Deserialize(ReadOnlySequence<byte> buffer, string manifest)
        {
            switch (manifest)
            {
                case StringManifest:
                case StringManifestNetCore:
                case StringManifestNetFx:
                    return DecodeString(buffer);

                case Int32Manifest:
                case Int32ManifestNetCore:
                case Int32ManifestNetFx:
                    return DecodeInt32(buffer);

                case Int64Manifest:
                case Int64ManifestNetCore:
                case Int64ManifestNetFx:
                    return DecodeInt64(buffer);

                default:
                    throw new ArgumentException($"Unimplemented deserialization of message with manifest [{manifest}] in [{GetType()}]");
            }
        }

        /// <inheritdoc/>
        public override byte[] ToBinary(object obj)
        {
            // Override the bridge to skip the ArrayBufferWriter round trip — for fixed-width
            // ints we can size precisely and write directly into a freshly-allocated array.
            // For strings, Encoding.UTF8.GetBytes(string) is well-tuned and matches V1 exactly.
            return obj switch
            {
                string s => Encoding.UTF8.GetBytes(s),
                int i => BitConverter.IsLittleEndian ? BitConverter.GetBytes(i) : Int32ToLittleEndianBytes(i),
                long l => BitConverter.IsLittleEndian ? BitConverter.GetBytes(l) : Int64ToLittleEndianBytes(l),
                _ => throw new ArgumentException($"Cannot serialize object of type [{obj.GetType()}]")
            };
        }

        /// <inheritdoc/>
        public override object FromBinary(byte[] bytes, string manifest)
        {
            // Override the bridge to skip the ReadOnlySequence wrapping for the common
            // contiguous-byte[] case.
            switch (manifest)
            {
                case StringManifest:
                case StringManifestNetCore:
                case StringManifestNetFx:
                    return Encoding.UTF8.GetString(bytes);

                case Int32Manifest:
                case Int32ManifestNetCore:
                case Int32ManifestNetFx:
                    return BinaryPrimitives.ReadInt32LittleEndian(bytes);

                case Int64Manifest:
                case Int64ManifestNetCore:
                case Int64ManifestNetFx:
                    return BinaryPrimitives.ReadInt64LittleEndian(bytes);

                default:
                    throw new ArgumentException($"Unimplemented deserialization of message with manifest [{manifest}] in [{GetType()}]");
            }
        }

        private static string DecodeString(ReadOnlySequence<byte> buffer)
        {
            if (buffer.IsSingleSegment)
                return Encoding.UTF8.GetString(buffer.First.Span);

            // Multi-segment path: Encoding.UTF8.GetString natively accepts a
            // ReadOnlySequence<byte> on net6+ and handles split codepoints across segments.
            return Encoding.UTF8.GetString(buffer);
        }

        private static int DecodeInt32(ReadOnlySequence<byte> buffer)
        {
            if (buffer.FirstSpan.Length >= sizeof(int))
                return BinaryPrimitives.ReadInt32LittleEndian(buffer.FirstSpan);

            // Slow path — value spans a segment boundary. Copy to a stack buffer.
            Span<byte> tmp = stackalloc byte[sizeof(int)];
            buffer.Slice(0, sizeof(int)).CopyTo(tmp);
            return BinaryPrimitives.ReadInt32LittleEndian(tmp);
        }

        private static long DecodeInt64(ReadOnlySequence<byte> buffer)
        {
            if (buffer.FirstSpan.Length >= sizeof(long))
                return BinaryPrimitives.ReadInt64LittleEndian(buffer.FirstSpan);

            Span<byte> tmp = stackalloc byte[sizeof(long)];
            buffer.Slice(0, sizeof(long)).CopyTo(tmp);
            return BinaryPrimitives.ReadInt64LittleEndian(tmp);
        }

        private static byte[] Int32ToLittleEndianBytes(int value)
        {
            var bytes = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            return bytes;
        }

        private static byte[] Int64ToLittleEndianBytes(long value)
        {
            var bytes = new byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
            return bytes;
        }
    }
}
