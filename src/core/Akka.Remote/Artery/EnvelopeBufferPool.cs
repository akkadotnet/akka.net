using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using Akka.Actor;
using Akka.Pattern;
using Akka.Remote.Artery.Compress;
using Akka.Remote.Artery.Internal;
using Akka.Remote.Serialization;
using Akka.Serialization;
using Akka.Util;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class OutOfBufferException : AkkaException
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
                buffer.ByteBuffer.Clear();
                return buffer;
            }
            
            return new EnvelopeBuffer(new ByteBuffer(MaximumPayload));
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

        bool UseOutboundCompression { get; set; }

        CompressionTable<IActorRef> OutboundActorRefCompression { get; set; }

        CompressionTable<string> OutboundClassManifestCompression { get; set; }

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

        public string SenderActorRef { get; internal set; } = null;
        public int SenderActorRefIdx { get; internal set; } = -1;
        public string RecipientActorRef { get; internal set; } = null;
        public int RecipientActorRefIdx { get; internal set; } = -1;

        public string Manifest { get; internal set; } = null;
        public int ManifestIdx { get; internal set; } = -1;

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

            Serializer = 0;
            Manifest = null;
            ManifestIdx = -1;

            RemoteInstruments = null;
        }

        public IInboundCompressions InboundCompression { get; }

        public byte Version { get; set; }
        public byte Flags { get; set; }

        public bool Flag(ByteFlag byteFlag) => (Flags & byteFlag.Mask) != 0;
        public void SetFlag(ByteFlag byteFlag) => Flags |= byteFlag.Mask;
        public void ClearFlag(ByteFlag byteFlag) => Flags &= (byte)(~(byteFlag.Mask));

        public long Uid { get; set; }

        public byte InboundActorRefCompressionTableVersion { get; set; }
        public byte InboundClassManifestCompressionTableVersion { get; set; }

        public bool UseOutboundCompression { get; set; } = true;

        public CompressionTable<IActorRef> OutboundActorRefCompression { get; set; }
        public CompressionTable<string> OutboundClassManifestCompression { get; set; }

        public void SetSenderActorRef(IActorRef @ref)
        {
            if(UseOutboundCompression)
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
                return InboundCompression.DecompressActorRef(
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
            if (UseOutboundCompression)
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
            if (UseOutboundCompression)
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
            IInboundCompressions inboundCompressions,
            CompressionTable<IActorRef> outboundActorRefCompression,
            CompressionTable<string> outboundClassManifestCompression)
        {
            InboundCompression = inboundCompressions;
            OutboundActorRefCompression = outboundActorRefCompression;
            OutboundClassManifestCompression = outboundClassManifestCompression;
        }

        public override string ToString()
            => "HeaderBuilderImpl(" +
            $"Version:{Version}, " +
            $"Flags:{ByteFlag.BinaryLeftPad(Flags)}, " +
            $"UID:{Uid}, " +
            $"SenderActorRef:{SenderActorRef}, " +
            $"SenderActorRefIdx:{SenderActorRefIdx}, " +
            $"RecipientActorRef:{RecipientActorRef}, " +
            $"RecipientActorRefIdx:{RecipientActorRefIdx}, " +
            $"Serializer:{Serializer}, " +
            $"Manifest:{Manifest}, " +
            $"ManifestIdx:{ManifestIdx})";
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
        public const int TagValueMask = 0x0000FFFF;

        // Flags (1 byte allocated for them)
        private static readonly ByteFlag MetadataPresentFlag = new ByteFlag(0x1);

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

        public ByteBuffer ByteBuffer { get; }

        // The streamId is only used for TCP transport. 
        // It is not part of the ordinary envelope header, 
        // but included in the frame header that is parsed by the TcpFraming stage.
        private int _streamId = -1;
        public int StreamId
        {
            get => _streamId != -1 ? _streamId : throw new IllegalStateException("StreamId was not set.");
            set => _streamId = value;
        }

        public EnvelopeBuffer(ByteBuffer byteBuffer)
        {
            ByteBuffer = byteBuffer;
        }

        public void WriteHeader(IHeaderBuilder h)
        {
            WriteHeader(h, null);
        }

        public void WriteHeader(IHeaderBuilder h, IOutboundEnvelope oe)
        {
            var header = (HeaderBuilderImpl)h;
            var buffer = ByteBuffer;

            // Write fixed length parts
            buffer.Put(VersionOffset, header.Version);
            buffer.Put(FlagsOffset, header.Flags);

            // compression table version numbers
            buffer.Put(ActorRefCompressionTableVersionOffset, header.OutboundActorRefCompression.Version);
            buffer.Put(ClassManifestCompressionTableVersionOffset, header.OutboundClassManifestCompression.Version);
            buffer.PutLong(UidOffset, header.Uid);
            buffer.PutInt(SerializerOffset, header.Serializer);

            // maybe write some metadata
            // after metadata is written (or not), buffer is at correct position to continue writing literals
            buffer.Position = MetadataContainerAndLiteralSectionOffset;
            if (header.RemoteInstruments.HasValue)
            {
                header.RemoteInstruments.Value.Serialize(new Option<IOutboundEnvelope>(oe), buffer);
                if (buffer.Position != MetadataContainerAndLiteralSectionOffset)
                {
                    // we actually wrote some metadata so update the flag field to reflect that
                    header.SetFlag(MetadataPresentFlag);
                    buffer.Put(FlagsOffset, header.Flags);
                }
            }

            // Serialize sender
            if (header.SenderActorRefIdx != -1)
                buffer.PutInt(SenderActorRefTagOffset, (int)(header.SenderActorRefIdx | TagTypeMask));
            else
                WriteLiteral(SenderActorRefTagOffset, header.SenderActorRef);

            // Serialize recipient
            if (header.RecipientActorRefIdx != -1)
                buffer.PutInt(RecipientActorRefTagOffset, (int)(header.RecipientActorRefIdx | TagTypeMask));
            else
                WriteLiteral(RecipientActorRefTagOffset, header.RecipientActorRef);

            // Serialize class manifest
            if (header.ManifestIdx != -1)
                buffer.PutInt(ClassManifestTagOffset, (int)(header.ManifestIdx | TagTypeMask));
            else
                WriteLiteral(ClassManifestTagOffset, header.Manifest);
        }

        public void ParseHeader(IHeaderBuilder h)
        {
            var header = (HeaderBuilderImpl)h;
            var buffer = ByteBuffer;
            buffer.Position = 0;

            // Read fixed length parts
            header.Version = buffer.Get(VersionOffset);

            if (header.Version > ArteryTransport.HighestVersion)
                throw new ArgumentException($"Incompatible protocol version [{header.Version}], " +
                    $"highest known version for this node is [{ArteryTransport.HighestVersion}]");

            header.Flags = buffer.Get(FlagsOffset);
            // compression table versions (stored in the Tag)
            header.InboundActorRefCompressionTableVersion = buffer.Get(ActorRefCompressionTableVersionOffset);
            header.InboundClassManifestCompressionTableVersion =
                buffer.Get(ClassManifestCompressionTableVersionOffset);
            header.Uid = buffer.GetLong(UidOffset);
            header.Serializer = buffer.GetInt(SerializerOffset);

            buffer.Position = MetadataContainerAndLiteralSectionOffset;
            if (header.Flag(MetadataPresentFlag))
            {
                // metadata present, so we need to fast forward to the literals that start right after
                var totalMetadataLength = buffer.GetInt();
                buffer.Position += totalMetadataLength;
            }

            // deserialize sender
            var senderTag = buffer.GetInt(SenderActorRefTagOffset);
            if ((senderTag & TagTypeMask) != 0)
            {
                var idx = senderTag & TagValueMask;
                header.SenderActorRef = null;
                header.SenderActorRefIdx = idx;
            }
            else
            {
                header.SenderActorRef = EmptyAsNull(ReadLiteral());
            }

            // deserialize recipient
            var recipientTag = buffer.GetInt(RecipientActorRefTagOffset);
            if ((recipientTag & TagTypeMask) != 0)
            {
                var idx = recipientTag & TagValueMask;
                header.RecipientActorRef = null;
                header.RecipientActorRefIdx = idx;
            }
            else
            {
                header.RecipientActorRef = EmptyAsNull(ReadLiteral());
            }

            // deserialize class manifest
            var manifestTag = buffer.GetInt(ClassManifestTagOffset);
            if ((manifestTag & TagTypeMask) != 0)
            {
                var idx = manifestTag & TagValueMask;
                header.Manifest = null;
                header.ManifestIdx = idx;
            }
            else
            {
                header.Manifest = EmptyAsNull(ReadLiteral());
            }
        }

        private void WriteLiteral(int tagOffset, string literal)
        {
            if (literal is null) literal = "";
            var length = literal.Length;
            if (length > 65535)
                throw new IllegalArgumentException("Literals longer than 65535 cannot be encoded in the envelope");

            ByteBuffer.PutInt(tagOffset, (int)ByteBuffer.Position);
            ByteBuffer.PutShort((ushort)length);
            if (length > 0)
                ByteBuffer.Put(Encoding.UTF8.GetBytes(literal), 0, length);
        }

        private string ReadLiteral()
        {
            var length = ByteBuffer.GetUShort();
            return length == 0 ? "" : Encoding.UTF8.GetString(ByteBuffer.GetBytes(length));
        }

        private static string EmptyAsNull(string s)
            => string.IsNullOrEmpty(s) ? null : s;

        public EnvelopeBuffer Copy()
            => new EnvelopeBuffer(ByteBuffer.Clone());
    }
}
