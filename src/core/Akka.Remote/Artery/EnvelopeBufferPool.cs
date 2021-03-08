using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using Akka.Actor;
using Akka.IO;
using Akka.Pattern;
using Akka.Remote.Artery.Compress;
using Akka.Remote.Artery.Internal;
using Akka.Remote.Artery.Utils;
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

            var newBuf = new EnvelopeBuffer(ByteBuffer.Allocate(MaximumPayload));
            newBuf.ByteBuffer.Order(ByteOrder.LittleEndian);
            return newBuf;
        }

        public void Release(EnvelopeBuffer buffer)
        {
            // this simulates a capacity bound queue, we'll probably overshoot the capacity under load.
            if (_availableBuffers.Count >= MaximumBuffers)
            {
                buffer.Dispose();
                return;
            }
            
            _availableBuffers.Enqueue(buffer);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal readonly struct ByteFlag
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
                NoInboundCompressions.Instance,
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

        byte Flags { get; }
        bool Flag(ByteFlag byteFlag);
        void SetFlag(ByteFlag byteFlag);
        void ClearFlag(ByteFlag byteFlag);

        byte InboundActorRefCompressionTableVersion { get; }
        byte InboundClassManifestCompressionTableVersion { get; }

        void UseOutboundCompression(bool on);

        CompressionTable<IActorRef> OutboundActorRefCompression { get; set; }

        CompressionTable<string> OutboundClassManifestCompression { get; set; }

        long Uid { get; set; }

        void SetSenderActorRef(IActorRef @ref);

        /// <summary>
        /// Retrieve the compressed ActorRef by the compressionId carried by this header.
        /// Returns `None` if ActorRef was not compressed, and then the literal 
        /// [[senderActorRefPath]] should be used.
        /// </summary>
        /// <param name="originUid"></param>
        /// <returns></returns>
        IOptionVal<IActorRef> SenderActorRef(long originUid);

        /// <summary>
        /// Retrieve the raw literal actor path, instead of using the compressed value.
        /// Returns `None` if ActorRef was compressed (!). 
        /// To obtain the path in such case call [[senderActorRef]] and extract the path from it directly.
        /// </summary>
        IOptionVal<string> SenderActorRefPath { get; }

        void SetNoSender();
        bool IsNoSender { get; }

        void SetNoRecipient();
        bool IsNoRecipient { get; }

        void SetRecipientActorRef(IActorRef @ref);

        /// <summary>
        /// Retrieve the compressed ActorRef by the compressionId carried by this header.
        /// Returns `None` if ActorRef was not compressed, and then the literal 
        /// [[recipientActorRefPath]] should be used.
        /// </summary>
        /// <param name="originUid"></param>
        /// <returns></returns>
        IOptionVal<IActorRef> RecipientActorRef(long originUid);

        /// <summary>
        /// Retrieve the raw literal actor path, instead of using the compressed value.
        /// Returns `None` if ActorRef was compressed (!). 
        /// To obtain the path in such case call [[recipientActorRefPath]] and extract the path from it directly.
        /// </summary>
        IOptionVal<string> RecipientActorRefPath { get; }

        int Serializer { get; set; }

        void SetManifest(string manifest);
        IOptionVal<string> Manifest(long originUid);

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

        protected override string Compute(IActorRef @ref)
            => Akka.Serialization.Serialization.SerializedActorPath(@ref);

        // Not calling ref.hashCode since it does a path.hashCode if ActorCell.undefinedUid is encountered.
        // Refs with ActorCell.undefinedUid will now collide all the time, but this is not a usual scenario anyway.
        // ARTERY: casting long to int, possible overflow problem?
        protected override int Hash(IActorRef k)
            => (int) k.Path.Uid;

        protected override bool IsCacheable(string v) => true;
    }

    internal sealed class HeaderBuilderImpl : IHeaderBuilder
    {
        private static SerializationFormatCache ToSerializationFormat => new SerializationFormatCache();

        private readonly IInboundCompressions _inboundCompression;
        private CompressionTable<IActorRef> _outboundActorRefCompression;
        private CompressionTable<string> _outboundClassManifestCompression;


        private byte _version = (byte)0;
        private byte _flags = (byte)0;
        private long _uid = 0L;
        internal byte _inboundActorRefCompressionTableVersion = (byte)0;
        internal byte _inboundClassManifestCompressionTableVersion = (byte)0;
        private bool _useOutboundCompression = true;

        internal string _senderActorRef = null;
        internal int _senderActorRefIdx = -1;
        internal string _recipientActorRef = null;
        internal int _recipientActorRefIdx = -1;

        private int _serializer = 0;
        internal string _manifest = null;
        internal int _manifestIdx = -1;

        public void ResetMessageFields()
        {
            // some fields must not be reset because they are set only once from the Encoder,
            // which owns the HeaderBuilder instance. Those are never changed.
            // version, uid, streamId

            _flags = 0;
            _senderActorRef = null;
            _senderActorRefIdx = -1;
            _recipientActorRef = null;
            _recipientActorRefIdx = - 1;

            _serializer = 0;
            _manifest = null;
            _manifestIdx = -1;
        }

        public byte Version
        {
            get => _version;
            set => _version = value;
        }

        public byte Flags
        {
            get => _flags;
            set => _flags = value;
        }

        public bool Flag(ByteFlag byteFlag) => (_flags & byteFlag.Mask) != 0;
        public void SetFlag(ByteFlag byteFlag) => _flags = (byte)(Flags | byteFlag.Mask);
        public void ClearFlag(ByteFlag byteFlag) => _flags = (byte)(Flags & ~(byteFlag.Mask));

        public long Uid
        {
            get => _uid;
            set => _uid = value;
        }

        public byte InboundActorRefCompressionTableVersion => _inboundActorRefCompressionTableVersion;
        public byte InboundClassManifestCompressionTableVersion => _inboundClassManifestCompressionTableVersion;

        public void UseOutboundCompression(bool on) => _useOutboundCompression = on;

        public CompressionTable<IActorRef> OutboundActorRefCompression
        {
            get => _outboundActorRefCompression;
            set => _outboundActorRefCompression = value;
        }

        public CompressionTable<string> OutboundClassManifestCompression
        {
            get => _outboundClassManifestCompression;
            set => _outboundClassManifestCompression = value;
        }

        public void SetSenderActorRef(IActorRef @ref)
        {
            if(_useOutboundCompression)
            {
                _senderActorRefIdx = OutboundActorRefCompression.Compress(@ref);
                if (_senderActorRefIdx == -1)
                    _senderActorRef = Akka.Serialization.Serialization.SerializedActorPath(@ref);
            }
            else
            {
                _senderActorRef = Akka.Serialization.Serialization.SerializedActorPath(@ref);
            }
        }
        public void SetNoSender()
        {
            _senderActorRef = null;
            _senderActorRefIdx = HeaderBuilder.DeadLettersCode;
        }
        public bool IsNoSender 
            => _senderActorRef is null && _senderActorRefIdx == HeaderBuilder.DeadLettersCode;
        public IOptionVal<IActorRef> SenderActorRef(long originUid)
        {
            // we treat deadLetters as always present, but not included in table
            if (_senderActorRef is null && !IsNoSender)
                return _inboundCompression.DecompressActorRef(
                    originUid,
                    InboundActorRefCompressionTableVersion,
                    _senderActorRefIdx);
            else
                return OptionVal.None<IActorRef>();
        }

        public IOptionVal<string> SenderActorRefPath => OptionVal.Some(_senderActorRef);

        public void SetNoRecipient()
        {
            _recipientActorRef = null;
            _recipientActorRefIdx = HeaderBuilder.DeadLettersCode;
        }

        public bool IsNoRecipient 
            => _recipientActorRef is null && _recipientActorRefIdx == HeaderBuilder.DeadLettersCode;

        // Note that Serialization.currentTransportInformation must be set when calling this method,
        // because it's using `Serialization.serializedActorPath`
        public void SetRecipientActorRef(IActorRef @ref)
        {
            if (_useOutboundCompression)
            {
                _recipientActorRefIdx = OutboundActorRefCompression.Compress(@ref);
                if (_recipientActorRefIdx == -1) 
                    _recipientActorRef = ToSerializationFormat.GetOrCompute(@ref);
            } 
            else
            {
                _recipientActorRef = ToSerializationFormat.GetOrCompute(@ref);
            }
        }
        public IOptionVal<IActorRef> RecipientActorRef(long originUid)
        {
            // we treat deadLetters as always present, but not included in table
            if (_recipientActorRef is null && !IsNoRecipient)
                return _inboundCompression.DecompressActorRef(
                    originUid,
                    InboundActorRefCompressionTableVersion,
                    _recipientActorRefIdx);
            else
                return OptionVal.None<IActorRef>();
        }
        public IOptionVal<string> RecipientActorRefPath => OptionVal.Some(_recipientActorRef);

        public int Serializer
        {
            get => _serializer;
            set => _serializer = value;
        }

        public void SetManifest(string manifest)
        {
            if (_useOutboundCompression)
            {
                _manifestIdx = OutboundClassManifestCompression.Compress(manifest);
                if (_manifestIdx == -1) _manifest = manifest;
            } else
            {
                _manifest = manifest;
            }
        }
        public IOptionVal<string> Manifest(long originUid)
        {
            if (_manifest != null)
                return OptionVal.Some(_manifest);
            else
                return _inboundCompression.DecompressClassManifest(
                    originUid,
                    InboundClassManifestCompressionTableVersion,
                    _manifestIdx);
        }

        public HeaderBuilderImpl(
            IInboundCompressions inboundCompressions,
            CompressionTable<IActorRef> outboundActorRefCompression,
            CompressionTable<string> outboundClassManifestCompression)
        {
            _inboundCompression = inboundCompressions;
            _outboundActorRefCompression = outboundActorRefCompression;
            _outboundClassManifestCompression = outboundClassManifestCompression;
        }

        public override string ToString()
            => "HeaderBuilderImpl(" +
            $"version:{_version}, " +
            $"flags:{ByteFlag.BinaryLeftPad(_flags)}, " +
            $"UID:{_uid}, " +
            $"_senderActorRef:{_senderActorRef}, " +
            $"_senderActorRefIdx:{_senderActorRefIdx}, " +
            $"_recipientActorRef:{_recipientActorRef}, " +
            $"_recipientActorRefIdx:{_recipientActorRefIdx}, " +
            $"_serializer:{Serializer}, " +
            $"_manifest:{_manifest}, " +
            $"_manifestIdx:{_manifestIdx})";
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
    internal sealed class EnvelopeBuffer : IDisposable
    {
        public const uint TagTypeMask = 0xFF000000;
        public const int TagValueMask = 0x0000FFFF;

        // Flags (1 byte allocated for them)
        public static readonly ByteFlag MetadataPresentFlag = new ByteFlag(0x1);

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

        private byte[] _literalBytes = new byte[64];

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
            => WriteHeader(h, null);

        public void WriteHeader(IHeaderBuilder h, IOutboundEnvelope oe)
        {
            var header = (HeaderBuilderImpl)h;
            var buffer = ByteBuffer;
            buffer.Clear();

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
            // NOTE: For Akka.Net, we do not have any metadata that needed writing at the moment, but if we do,
            //       this is the place to inject it (plus the sample code)
            /*
            buffer.Position(MetadataContainerAndLiteralSectionOffset);
            if (header._remoteInstruments.IsDefined)
            {
                header._remoteInstruments.Get.Serialize(OptionVal.Apply(oe), buffer);
                if (buffer.Position() != MetadataContainerAndLiteralSectionOffset)
                {
                    // we actually wrote some metadata so update the flag field to reflect that
                    header.SetFlag(MetadataPresentFlag);
                    buffer.Put(FlagsOffset, header.Flags);
                }
            }
            */

            // Serialize sender
            if (header._senderActorRefIdx != -1)
                buffer.PutInt(SenderActorRefTagOffset, (int)(header._senderActorRefIdx | TagTypeMask));
            else
                WriteLiteral(SenderActorRefTagOffset, header._senderActorRef);

            // Serialize recipient
            if (header._recipientActorRefIdx != -1)
                buffer.PutInt(RecipientActorRefTagOffset, (int)(header._recipientActorRefIdx | TagTypeMask));
            else
                WriteLiteral(RecipientActorRefTagOffset, header._recipientActorRef);

            // Serialize class manifest
            if (header._manifestIdx != -1)
                buffer.PutInt(ClassManifestTagOffset, (int)(header._manifestIdx | TagTypeMask));
            else
                WriteLiteral(ClassManifestTagOffset, header._manifest);
        }

        public void ParseHeader(IHeaderBuilder h)
        {
            var header = (HeaderBuilderImpl)h;
            var buffer = ByteBuffer;

            // Read fixed length parts
            header.Version = buffer.Get(VersionOffset);

            if (header.Version > ArteryTransport.HighestVersion)
                throw new IllegalArgumentException(
                    $"Incompatible protocol version [{header.Version}], " +
                    $"highest known version for this node is [{ArteryTransport.HighestVersion}]");

            header.Flags = buffer.Get(FlagsOffset);
            // compression table versions (stored in the Tag)
            header._inboundActorRefCompressionTableVersion = buffer.Get(ActorRefCompressionTableVersionOffset);
            header._inboundClassManifestCompressionTableVersion =
                buffer.Get(ClassManifestCompressionTableVersionOffset);
            header.Uid = buffer.GetLong(UidOffset);
            header.Serializer = buffer.GetInt(SerializerOffset);

            buffer.Position(MetadataContainerAndLiteralSectionOffset);
            if (header.Flag(MetadataPresentFlag))
            {
                // metadata present, so we need to fast forward to the literals that start right after
                var totalMetadataLength = buffer.GetInt();
                buffer.Position(buffer.Position() + totalMetadataLength);
            }

            // deserialize sender
            var senderTag = buffer.GetInt(SenderActorRefTagOffset);
            if ((senderTag & TagTypeMask) != 0)
            {
                var idx = senderTag & TagValueMask;
                header._senderActorRef = null;
                header._senderActorRefIdx = idx;
            }
            else
            {
                header._senderActorRef = EmptyAsNull(ReadLiteral());
            }

            // deserialize recipient
            var recipientTag = buffer.GetInt(RecipientActorRefTagOffset);
            if ((recipientTag & TagTypeMask) != 0)
            {
                var idx = recipientTag & TagValueMask;
                header._recipientActorRef = null;
                header._recipientActorRefIdx = idx;
            }
            else
            {
                header._recipientActorRef = EmptyAsNull(ReadLiteral());
            }

            // deserialize class manifest
            var manifestTag = buffer.GetInt(ClassManifestTagOffset);
            if ((manifestTag & TagTypeMask) != 0)
            {
                var idx = manifestTag & TagValueMask;
                header._manifest = null;
                header._manifestIdx = idx;
            }
            else
            {
                header._manifest = EmptyAsNull(ReadLiteral());
            }
        }

        private static string EmptyAsNull(string s)
            => string.IsNullOrEmpty(s) ? null : s;

        private string ReadLiteral()
        {
            // Up-cast to Int to avoid up-casting 4 times.
            var length = (int)ByteBuffer.GetUShort();
            if (length == 0)
                return "";

            EnsureLiteralCharsLength(length);
            var bytes = _literalBytes;
            ByteBuffer.Get(bytes, 0, length);
            return Encoding.ASCII.GetString(bytes, 0, length);
        }

        private void WriteLiteral(int tagOffset, string literal)
        {
            var length = literal?.Length ?? 0;
            if (length > 65535)
                throw new IllegalArgumentException("Literals longer than 65535 cannot be encoded in the envelope");

            ByteBuffer.PutInt(tagOffset, ByteBuffer.Position());
            ByteBuffer.PutShort((ushort)length);
            if (length > 0)
                ByteBuffer.Put(Encoding.UTF8.GetBytes(literal), 0, length);
        }

        private void EnsureLiteralCharsLength(int length)
        {
            if (length > _literalBytes.Length)
                _literalBytes = new byte[length];
        }

        public EnvelopeBuffer Copy()
        {
            var p = ByteBuffer.Position();
            ByteBuffer.Rewind();
            var bytes = new byte[ByteBuffer.Remaining];
            ByteBuffer.Get(bytes);
            var newByteBuffer = ByteBuffer.Wrap(bytes);
            newByteBuffer.Position(p);
            ByteBuffer.Position(p);
            return new EnvelopeBuffer(newByteBuffer);
        }

        public void Dispose()
        {
            ByteBuffer?.Dispose();
        }
    }
}
