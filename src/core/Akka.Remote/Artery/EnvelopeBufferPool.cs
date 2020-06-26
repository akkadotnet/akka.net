using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using Akka.Actor;
using Akka.Pattern;
using Akka.Remote.Serialization;
using Akka.Serialization;
using Akka.Util;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class OutOfBufferException : Exception
    {
        public OutOfBufferException():base("Out of usable ByteBuffers")
        {
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class EnvelopeBufferPool
    {
        private readonly ConcurrentQueue<EnvelopeBuffer> _availableBuffers;

        public int MaximumPayload { get; }
        public int MaximumBuffers { get; }

        public EnvelopeBufferPool(int maximumPayload, int maximumBuffers)
        {
            MaximumPayload = maximumPayload;
            MaximumBuffers = maximumBuffers;
            _availableBuffers = new ConcurrentQueue<EnvelopeBuffer>();
        }

        public EnvelopeBuffer Acquire()
        {
            if (_availableBuffers.TryDequeue(out var buffer))
            {
                var underlying = buffer.ByteBuffer.GetBuffer();
                Array.Clear(underlying, 0, underlying.Length);
                return buffer;
            }
            
            return new EnvelopeBuffer(new MemoryStream(new byte[MaximumPayload]));
        }

        public void Release(EnvelopeBuffer buffer)
        {
            if (_availableBuffers.Count >= MaximumBuffers)
                return;

            _availableBuffers.Enqueue(buffer);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ByteFlag
    {
        public static string BinaryLeftPad(byte value)
            => Convert.ToString(value, 2).PadLeft(8, '0');

        public byte Mask { get; }

        public ByteFlag(byte mask)
        {
            Mask = mask;
        }

        public bool IsEnabled(byte byteMask) => (byteMask & Mask) != 0;

        public override string ToString()
            => $"ByteFlag({BinaryLeftPad(Mask)})";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal static class HeaderBuilder
    {
        // We really only use the Header builder on one "side" or the other, 
        // thus in order to avoid having to split its impl
        // we inject no-op compression's of the "other side".

        public static HeaderBuilderImpl In(IInboundCompressions compression)
            => new HeaderBuilderImpl(compression,
                CompressionTable<IActorRef>.Empty,
                CompressionTable<string>.Empty);

        public static HeaderBuilderImpl Out()
            => new HeaderBuilderImpl(
                new NoInboundCompressions(),
                CompressionTable<IActorRef>.Empty,
                CompressionTable<string>.Empty);

        public const int DeadLettersCode = -1;
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal interface IHeaderBuilder
    {
        byte Version { get; set; }

        byte Flags { get; set; }

        bool Flag(ByteFlag byteFlag);
        void SetFlag(ByteFlag byteFlag);
        void ClearFlag(ByteFlag byteFlag);

        byte InboundActorRefCompressionTableVersion { get; set; }
        byte InboundClassManifestCompressionTableVersion { get; set; }

        void UseOutboundCompression(bool on);

        CompressionTable<IActorRef> OutboundActorRefCompression { get; set; }

        CompressionTable<IActorRef> OutboundClassManifestCompression { get; set; }

        long Uid { get; set; }

        void SetSenderActorRef(IActorRef @ref);

        /// <summary>
        /// Retrive the compressed ActorRef by the compressionId carried by this header.
        /// Returns `None` if ActorRef was not compressed, and then the literal 
        /// [[senderActorRefPath]] should be used.
        /// </summary>
        /// <param name="originUid"></param>
        /// <returns></returns>
        Option<IActorRef> GetSenderActorRef(long originUid);

        /// <summary>
        /// Retrive the raw literal actor path, instead of using the compressed value.
        /// Returns `None` if ActorRef was compressed (!). 
        /// To obtain the path in such case call [[senderActorRef]] and extract the path from it directly.
        /// </summary>
        Option<string> SenderActorRefPath { get; }

        void SetNoSender();
        bool IsNoSender { get; }

        void SetNoRecipient();
        bool IsNoRecipient { get; }

        void SetRecipientActorRef(IActorRef @ref);

        /// <summary>
        /// Retrive the compressed ActorRef by the compressionId carried by this header.
        /// Returns `None` if ActorRef was not compressed, and then the literal 
        /// [[recipientActorRefPath]] should be used.
        /// </summary>
        /// <param name="originUid"></param>
        /// <returns></returns>
        Option<IActorRef> GetRecipientActorRef(long originUid);

        /// <summary>
        /// Retrive the raw literal actor path, instead of using the compressed value.
        /// Returns `None` if ActorRef was compressed (!). 
        /// To obtain the path in such case call [[recipientActorRefPath]] and extract the path from it directly.
        /// </summary>
        Option<string> RecipientActorRefPath { get; }

        int Serializer { get; set; }

        void SetManifest(string manifest);
        Option<string> GetManifest(long originUid);

        Option<RemoteInstruments> RemoteInstruments { get; set; }

        /// <summary>
        /// Reset all fields that are related to an outbound message,
        /// i.e. Encoder calls this as the first thing in onPush.
        /// </summary>
        void ResetMessageFields();
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class SerializationFormatCache: LruBoundedCache<IActorRef, string>
    {
        public SerializationFormatCache(int capacity = 1024, int evictAgeThreshold = 600) :
            base(capacity, evictAgeThreshold)
        { }

        protected override string Compute(IActorRef k)
            => Akka.Serialization.Serialization.SerializedActorPath(k);

        // ARTERY: casting long to int, possible overflow problem?
        protected override int Hash(IActorRef k)
            => (int) k.Path.Uid;

        protected override bool IsCacheable(string v) => true;
    }

    internal sealed class HeaderBuilderImpl : IHeaderBuilder
    {
        internal SerializationFormatCache ToSerializationFormat()
            => new SerializationFormatCache();

        private bool _useOutboundCompression = true;

        public string SenderActorRef { get; private set; } = null;
        public int SenderActorRefIdx { get; private set; } = -1;
        public string RecipientActorRef { get; private set; } = null;
        public int RecipientActorRefIdx { get; private set; } = -1;

        private int _serializer = 0;
        public string Manifest { get; private set; } = null;
        public int ManifestIdx { get; private set; } = -1;

        public void ResetMessageFields()
        {
            // some fields must not be reset because they are set only once from the Encoder,
            // which owns the HeaderBuilder instance. Those are never changed.
            // version, uid, streamId

            Flags = 0;
            SenderActorRef = null;
            SenderActorRefIdx = -1;
            RecipientActorRef = null;
            RecipientActorRefIdx = - 1;

            _serializer = 0;
            Manifest = null;
            ManifestIdx = -1;

            RemoteInstruments = null;
        }

        public InboundCompressions InboundCompression { get; }

        public byte Version { get; set; }
        public byte Flags { get; set; }

        public bool Flag(ByteFlag byteFlag) => (Flags & byteFlag.Mask) != 0;
        public void SetFlag(ByteFlag byteFlag) => Flags |= byteFlag.Mask;
        public void ClearFlag(ByteFlag byteFlag) => Flags &= (byte)(~(byteFlag.Mask));

        public long Uid { get; set; }

        public byte InboundActorRefCompressionTableVersion { get; set; }
        public byte InboundClassManifestCompressionTableVersion { get; set; }

        public void UseOutboundCompression(bool on) => _useOutboundCompression = on;

        public CompressionTable<IActorRef> OutboundActorRefCompression { get; set; }
        public CompressionTable<IActorRef> OutboundClassManifestCompression { get; set; }

        public void SetSenderActorRef(IActorRef @ref)
        {
            if(_useOutboundCompression)
            {
                SenderActorRefIdx = OutboundActorRefCompression.Compress(@ref);
                if (SenderActorRefIdx == -1)
                    SenderActorRef = Akka.Serialization.Serialization.SerializedActorPath(@ref);
            }
            else
            {
                SenderActorRef = Akka.Serialization.Serialization.SerializedActorPath(@ref);
            }
        }
        public void SetNoSender()
        {
            SenderActorRef = null;
            SenderActorRefIdx = HeaderBuilder.DeadLettersCode;
        }
        public bool IsNoSender 
            => SenderActorRef is null && SenderActorRefIdx == HeaderBuilder.DeadLettersCode;
        public Option<IActorRef> GetSenderActorRef(long originUid)
        {
            // we treat deadLetters as always present, but not included in table
            if (SenderActorRef is null && !IsNoSender)
                return InboundCompression.decompressActorRef(
                    originUid,
                    InboundActorRefCompressionTableVersion,
                    SenderActorRefIdx);
            else
                return Option<IActorRef>.None;
        }

        public Option<string> SenderActorRefPath => new Option<string>(SenderActorRef);

        public void SetNoRecipient()
        {
            RecipientActorRef = null;
            RecipientActorRefIdx = HeaderBuilder.DeadLettersCode;
        }

        public bool IsNoRecipient 
            => RecipientActorRef is null && RecipientActorRefIdx == HeaderBuilder.DeadLettersCode;

        // Note that Serialization.currentTransportInformation must be set when calling this method,
        // because it's using `Serialization.serializedActorPath`
        public void SetRecipientActorRef(IActorRef @ref)
        {
            if (_useOutboundCompression)
            {
                RecipientActorRefIdx = OutboundActorRefCompression.Compress(@ref);
                if (RecipientActorRefIdx == -1) 
                    RecipientActorRef = ToSerializationFormat().GetOrCompute(@ref);
            } 
            else
            {
                RecipientActorRef = ToSerializationFormat().GetOrCompute(@ref);
            }
        }
        public Option<IActorRef> GetRecipientActorRef(long originUid)
        {
            // we treat deadLetters as always present, but not included in table
            if (RecipientActorRef is null && !IsNoRecipient)
                return InboundCompression.DecompressActorRef(
                    originUid,
                    InboundActorRefCompressionTableVersion,
                    RecipientActorRefIdx);
            else
                return Option<IActorRef>.None;
        }
        public Option<string> RecipientActorRefPath => new Option<string>(RecipientActorRef);

        public int Serializer { get; set; }

        public void SetManifest(string manifest)
        {
            if (_useOutboundCompression)
            {
                ManifestIdx = OutboundClassManifestCompression.Compress(manifest);
                if (ManifestIdx == -1) Manifest = manifest;
            } else
            {
                Manifest = manifest;
            }
        }
        public Option<string> GetManifest(long originUid)
        {
            if (Manifest is object)
                return new Option<string>(Manifest);
            else
                return InboundCompression.DecompressClassManifest(
                    originUid,
                    InboundClassManifestCompressionTableVersion,
                    ManifestIdx);
        }

        public Option<RemoteInstruments> RemoteInstruments { get; set; } = Option<RemoteInstruments>.None;

        public HeaderBuilderImpl( 
            InboundCompressions inboundCompressions,
            CompressionTable<IActorRef> outboundActorRefCompression,
            CompressionTable<string> outboundClassManifestCompression)
        {
            InboundCompression = inboundCompressions;
            OutboundActorRefCompression = outboundActorRefCompression;
            OutboundClassManifestCompression = outboundClassManifestCompression;
        }

        public override string ToString()
            => "HeaderBuilderImpl(" +
            $"version:{Version}, " +
            $"flags:{ByteFlag.BinaryLeftPad(Flags)}, " +
            $"UID:{Uid}, " +
            $"_senderActorRef:{SenderActorRef}, " +
            $"_senderActorRefIdx:{SenderActorRefIdx}, " +
            $"_recipientActorRef:{RecipientActorRef}, " +
            $"_recipientActorRefIdx:{RecipientActorRefIdx}, " +
            $"_serializer:{Serializer}, " +
            $"_manifest:{Manifest}, " +
            $"_manifestIdx:{ManifestIdx})";
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// The strategy if the header format must be changed in an incompatible way is:
    /// - In the end we only want to support one header format, the latest, but during
    ///   a rolling upgrade period we must support two versions in at least one Akka patch
    ///   release.
    /// - When supporting two version the outbound messages must still be encoded with old
    ///   version. The Decoder on the receiving side must understand both versions.
    /// - Create a new copy of the header encoding/decoding logic (issue #24553: 
    ///   we should refactor to make that easier).
    /// - Bump `ArteryTransport.HighestVersion` and keep `ArterySettings.Version` as the old version.
    /// - Make sure `Decoder` picks the right parsing logic based on the version field in the incoming frame.
    /// - Release Akka, e.g. 2.5.13
    /// - Later, remove the old header parsing logic and bump the `ArterySettings.Version` to the same as
    ///   `ArteryTransport.HighestVersion` again.
    /// - Release Akka, e.g. 2.5.14, and announce that all nodes in the cluster must first be on version
    ///   2.5.13 before upgrading to 2.5.14. That means that it is not supported to do a rolling upgrade
    ///   from 2.5.12 directly to 2.5.14.
    /// </summary>
    internal sealed class EnvelopeBuffer
    {
        public const uint TagTypeMask = 0xFF000000;
        public const uint TagValueMask = 0x0000FFFF;

        // Flags (1 byte allocated for them)
        private readonly ByteFlag _metadataPresentFlag = new ByteFlag(0x1);

        public const int VersionOffset = 0; // byte
        public const int FlagsOffset = 1; // byte
        public const int ActorRefCompressionTableVersionOffset = 2; // byte
        public const int ClassManifestCompressionTableVersionOffset = 3; // byte

        public const int UidOffset = 4; // long
        public const int SerializerOffset = 12; // int

        public const int SenderActorRefTagOffset = 16; // int
        public const int RecipientActorRefTagOffset = 20; // int
        public const int ClassManifestTagOffset = 24;

        // EITHER metadata followed by literals directly OR literals directly in this spot.
        // Mode depends on the `MetadataPresentFlag`.
        public const int MetadataContainerAndLiteralSectionOffset = 28; // int

        private MemoryStream _byteBuffer;
        private UnsafeBuffer _aeronBuffer;

        private char[] literalChars = new char[64];
        private byte[] literalBytes = new byte[64];

        // The streamId is only used for TCP transport. 
        // It is not part of the ordinary envelope header, 
        // but included in the frame header that is parsed by the TcpFraming stage.
        private int _streamId = -1;
        public int StreamId
        {
            get => _streamId != -1 ? _streamId : throw new IllegalStateException("StreamId was not set.");
            set => _streamId = value;
        }

        public EnvelopeBuffer(MemoryStream byteBuffer)
        {
            _byteBuffer = byteBuffer;
            _aeronBuffer = new UnsafeBuffer(byteBuffer.GetBuffer());
        }

        public void WriteHeader(IHeaderBuilder h)
        {
            WriteHeader(h, null);
        }

        public void WriteHeader(IHeaderBuilder h, OutboundEnvelope oe)
        {
            var header = (HeaderBuilderImpl)h;
            using (var buffer = new MemoryStream())
            using(var writer = new BinaryWriter(buffer))
            {
                // Write fixed length parts
                buffer.Position = VersionOffset;
                writer.Write(header.Version); // byte

                buffer.Position = FlagsOffset;
                writer.Write(header.Flags); // byte

                // compression table version numbers
                buffer.Position = ActorRefCompressionTableVersionOffset;
                writer.Write(header.OutboundActorRefCompression.Version); // byte

                buffer.Position = ClassManifestCompressionTableVersionOffset;
                writer.Write(header.OutboundClassManifestCompression.Version); // byte

                buffer.Position = UidOffset;
                writer.Write(header.Uid); // long

                buffer.Position = SerializerOffset;
                writer.Write(header.Serializer); // int

                // maybe write some metadata
                // after metadata is written (or not), buffer is at correct position to continue writing literals
                buffer.Position = MetadataContainerAndLiteralSectionOffset;
                if (header.RemoteInstruments.HasValue)
                {
                    header.RemoteInstruments.Value.Serialize(new Option<OutboundEnvelope>(oe), buffer);
                    if(buffer.Position != MetadataContainerAndLiteralSectionOffset)
                    {
                        // we actually wrote some metadata so update the flag field to reflect that
                        header.SetFlag(_metadataPresentFlag);
                        buffer.Position = FlagsOffset;
                        writer.Write(header.Flags);
                    }
                }

                // Serialize sender
                if(header.SenderActorRefIdx != -1)
                {
                    buffer.Position = SenderActorRefTagOffset;
                    writer.Write(header.SenderActorRefIdx | TagTypeMask);
                }
                else
                {
                    WriteLiteral(SenderActorRefTagOffset, header.SenderActorRef, buffer, writer);
                }

                // Serialize recipient
                if(header.RecipientActorRefIdx != -1)
                {
                    buffer.Position = RecipientActorRefTagOffset;
                    writer.Write(header.RecipientActorRefIdx | TagTypeMask);
                }
                else
                {
                    WriteLiteral(RecipientActorRefTagOffset, header.RecipientActorRef, buffer, writer);
                }

                // Serialize class manifest
                if(header.ManifestIdx != -1)
                {
                    buffer.Position = ClassManifestTagOffset;
                    writer.Write(header.ManifestIdx | TagTypeMask);
                }
                else
                {
                    WriteLiteral(ClassManifestTagOffset, header.Manifest, buffer, writer);
                }

                Array.Copy(buffer.GetBuffer(), _byteBuffer.GetBuffer(), buffer.Length);
            }
        }

        public void ParseHeader(IHeaderBuilder h)
        {
            var header = (HeaderBuilderImpl)h;

            using(var reader = new BinaryReader(_byteBuffer))
            {
                // Read fixed length parts
                _byteBuffer.Position = VersionOffset;
                header.Version = reader.ReadByte();

                if (header.Version > ArteryTransport.HighestVersion)
                    throw new ArgumentException($"Incompatible protocol version [{header.Version}], " +
                        $"highest known version for this node is [{ArteryTransport.HighestVersion}]");

                _byteBuffer.Position = FlagsOffset;
                header.Flags = reader.ReadByte();
                // compression table versions (stored in the Tag)
                _byteBuffer.Position = ActorRefCompressionTableVersionOffset;
                header.InboundActorRefCompressionTableVersion = reader.ReadByte();

                _byteBuffer.Position = ClassManifestCompressionTableVersionOffset;
                header.InboundClassManifestCompressionTableVersion = reader.ReadByte();

                _byteBuffer.Position = UidOffset;
                header.Uid = reader.ReadInt64();

                _byteBuffer.Position = SerializerOffset;
                header.Serializer = reader.ReadInt32();

                _byteBuffer.Position = MetadataContainerAndLiteralSectionOffset;
                if (header.Flag(_metadataPresentFlag))
                {
                    // metadata present, so we need to fast forward to the literals that start right after
                    var totalMetadataLength = reader.ReadInt32();
                    _byteBuffer.Position += totalMetadataLength;
                }

                // ============================ last line
            }

        }

        private void WriteLiteral(int tagOffset, string literal, MemoryStream buffer, BinaryWriter writer)
        {
            var length = literal is null ? 0 : literal.Length;
            if (length > 65535)
                throw new ArgumentException("Literals longer than 65535 cannot be encoded in the envelope");

            buffer.Position = tagOffset;
            writer.Write((short)length);
            if(length > 0)
                writer.Write(Encoding.ASCII.GetBytes(literal));
        }
    }
}
