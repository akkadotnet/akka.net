using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Artery.Internal;
using Akka.Remote.Artery.Utils;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API
    ///
    /// Part of the monitoring SPI which allows attaching metadata to outbound remote messages,
    /// and reading in metadata from incoming messages.
    ///
    /// Multiple instruments are automatically handled, however they MUST NOT overlap in their identifiers.
    ///
    /// Instances of `RemoteInstrument` are created from configuration. A new instance of RemoteInstrument
    /// will be created for each encoder and decoder. It's only called from the operator, so if it doesn't
    /// delegate to any shared instance it doesn't have to be thread-safe.
    /// </summary>
    internal abstract class RemoteInstrument
    {
        /// <summary>
        /// Instrument identifier.
        ///
        /// MUST be >= 1 and < 32.
        ///
        /// Values between 1 and 7 are reserved for Akka internal use.
        /// </summary>
        public abstract byte Identifier { get; }

        /// <summary>
        /// Should the serialization be timed? Otherwise times are always 0.
        /// </summary>
        public virtual bool SerializationTimingEnabled { get; } = false;

        /// <summary>
        /// Called while serializing the message.
        ///
        /// Parameters MAY be `null` (except `message` and `buffer`)!
        /// </summary>
        /// <param name="recipient"></param>
        /// <param name="message"></param>
        /// <param name="sender"></param>
        /// <param name="buffer"></param>
        public abstract void RemoteWriteMetadata(IActorRef recipient, object message, IActorRef sender,
            ByteBuffer buffer);

        /// <summary>
        /// Called right before putting the message onto the wire.
        /// Parameters MAY be `null` (except `message` and `buffer`)!
        ///
        /// The `size` is the total serialized size in bytes of the complete message including akka specific headers and any
        /// `RemoteInstrument` metadata.
        /// If `serializationTimingEnabled` returns true, then `time` will be the total time it took to serialize all data
        /// in the message in nanoseconds, otherwise it is 0.
        /// </summary>
        /// <param name="recipient"></param>
        /// <param name="message"></param>
        /// <param name="sender"></param>
        /// <param name="size"></param>
        /// <param name="time"></param>
        public abstract void RemoteMessageSent(IActorRef recipient, object message, IActorRef sender, int size,
            long time);

        /// <summary>
        /// Called while deserializing the message once a message (containing a metadata field designated for this instrument) is found.
        /// </summary>
        /// <param name="recipient"></param>
        /// <param name="message"></param>
        /// <param name="sender"></param>
        /// <param name="buffer"></param>
        public abstract void RemoteReadMetadata(IActorRef recipient, object message, IActorRef sender,
            ByteBuffer buffer);

        /// <summary>
        /// Called when the message has been deserialized.
        ///
        /// The `size` is the total serialized size in bytes of the complete message including akka specific headers and any
        /// `RemoteInstrument` metadata.
        /// If `serializationTimingEnabled` returns true, then `time` will be the total time it took to deserialize all data
        /// in the message in nanoseconds, otherwise it is 0.
        /// </summary>
        /// <param name="recipient"></param>
        /// <param name="message"></param>
        /// <param name="sender"></param>
        /// <param name="size"></param>
        /// <param name="time"></param>
        public abstract void RemoteMessageReceived(IActorRef recipient, object message, IActorRef sender, int size,
            long time);
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// The metadata section is stored as raw bytes (prefixed with an Int length field,
    /// the same way as any other literal), however the internal structure of it is as follows:
    ///
    /// <code>
    /// Metadata entry:
    ///
    ///  0                   1                   2                   3
    ///  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// |   Key     |             Metadata entry length                   |
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// |                   ... metadata entry ...                        |
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// </code>
    /// </summary>
    internal sealed class RemoteInstruments
    {
        public static RemoteInstruments Apply(ExtendedActorSystem system)
            => new RemoteInstruments(system);

        private readonly ExtendedActorSystem _system;
        private readonly ILoggingAdapter _log;
        private readonly ImmutableList<RemoteInstrument> _instruments;

        public bool IsEmpty => _instruments.Count == 0;
        public bool NonEmpty => _instruments.Count != 0;
        public bool TimeSerialization { get; }

        public RemoteInstruments(ExtendedActorSystem system, ILoggingAdapter log) 
            : this(system, log, Create(system, log))
        {}

        public RemoteInstruments(ExtendedActorSystem system)
            : this(system, Logging.GetLogger(system, typeof(RemoteInstruments)))
        {}

        public RemoteInstruments(ExtendedActorSystem system, ILoggingAdapter log, ImmutableList<RemoteInstrument> instruments)
        {
            _system = system;
            _log = log;
            _instruments = instruments.Sort((x, y) => x.Identifier - y.Identifier);

            TimeSerialization = _instruments.Exists(ri => ri.SerializationTimingEnabled);
        }

        public void Serialize(Option<IOutboundEnvelope> outboundEnvelope, ByteBuffer buffer)
        {
            if (_instruments.Count > 0 && outboundEnvelope.HasValue)
            {
                var startPos = buffer.Position;
                var oe = outboundEnvelope.Value;
                try
                {
                    buffer.PutInt(0);
                    var dataPos = buffer.Position;
                    var i = 0;
                    while (i < _instruments.Count)
                    {
                        var rewindPos = buffer.Position;
                        var instrument = _instruments[i];
                        try
                        {
                            SerializeInstrument(instrument, oe, buffer);
                        }
                        catch (Exception e)
                        {
                            if (!e.NonFatal()) throw;
                            _log.Debug(
                                $"Skipping serialization of RemoteInstrument {instrument.Identifier} since it failed with {e.Message}");
                            buffer.Position = rewindPos;
                        }

                        ++i;
                    }

                    var endPos = buffer.Position;
                    if (endPos == dataPos)
                    {
                        // no instruments wrote anything so we need to rewind to start
                        buffer.Position = startPos;
                    }
                    else
                    {
                        // some instruments wrote data, so write the total length
                        buffer.PutInt((int)startPos, (int)(endPos - dataPos));
                    }
                }
                catch(Exception e)
                {
                    if (!e.NonFatal()) throw;
                    _log.Debug($"Skipping serialization of all RemoteInstruments due to unhandled failure {e.Message}");
                    buffer.Position = startPos;
                }
            }
        }

        private void SerializeInstrument(RemoteInstrument instrument, IOutboundEnvelope outboundEnvelope,
            ByteBuffer buffer)
        {
            var startPos = buffer.Position;
            buffer.PutInt(0);
            var dataPos = buffer.Position;
            instrument.RemoteWriteMetadata(
                outboundEnvelope.Recipient.GetOrElse(null),
                outboundEnvelope.Message,
                outboundEnvelope.Sender.GetOrElse(null),
                buffer);
            var endPos = buffer.Position;
            if (endPos == dataPos)
            {
                // if the instrument didn't write anything, then rewind to the start
                buffer.Position = startPos;
            }
            else
            {
                // the instrument wrote something so we need to write the identifier and length
                buffer.PutInt(startPos, CombineKeyLength(instrument.Identifier, (int)(endPos - dataPos)));
            }
        }

        public void Deserialize(IInboundEnvelope inboundEnvelope)
        {
            // ARTERY: NOT IMPLEMENTED
        }

        public void DeserializeRaw(IInboundEnvelope inboundEnvelope)
        {
            // ARTERY: NOT IMPLEMENTED
        }

        private void DeserializeInstrument(RemoteInstrument instrument, IInboundEnvelope inboundEnvelope,
            ByteBuffer buffer)
        {
            // ARTERY: NOT IMPLEMENTED
        }

        public void MessageSent(IOutboundEnvelope outboundEnvelope, int size, long time)
        {
            foreach (var instrument in _instruments)
            {
                try
                {
                    MessageSentInstrument(instrument, outboundEnvelope, size, time);
                }
                catch (Exception e)
                {
                    if (!e.NonFatal()) throw;
                    _log.Debug($"Message sent in RemoteInstrument {instrument.Identifier} failed with {e.Message}");
                }
            }
        }

        private void MessageSentInstrument(RemoteInstrument instrument, IOutboundEnvelope outboundEnvelope, int size, long time)
        {
            instrument.RemoteMessageSent(
                outboundEnvelope.Recipient.GetOrElse(null),
                outboundEnvelope.Message,
                outboundEnvelope.Sender.GetOrElse(null),
                size,
                time);
        }

        public void MessageReceived(IInboundEnvelope inboundEnvelope, int size, long time)
        {
            // ARTERY: NOT IMPLEMENTED
        }

        public void MessageReceivedInstrument(RemoteInstrument instrument, IInboundEnvelope inboundEnvelope, int size,
            long time)
        {
            // ARTERY: NOT IMPLEMENTED
        }

        private class RemoteInstrumentIdEnumerator : IEnumerator<int>
        {
            private readonly MemoryStream _buffer;
            private readonly BinaryReader _reader;
            private int _current;

            public RemoteInstrumentIdEnumerator(ByteBuffer buffer, int endPos)
            {
                if (endPos < buffer.Position) return;

                _buffer = new MemoryStream(buffer.GetBuffer(), (int)buffer.Position, (int)(endPos - buffer.Position));
                _reader = new BinaryReader(_buffer);

                Reset();
            }

            public bool MoveNext()
            {
                var len = GetLength(_current);
                if (_buffer is null || len + _buffer.Position >= _buffer.Length)
                    return false;

                _buffer.Position += GetLength(_current);
                _current = _reader.ReadInt32();
                return true;
            }

            public void Reset()
            {
                _buffer.Position = 0;
                _current = _reader.ReadInt32();
            }

            public int Current => GetKey(_current);

            object IEnumerator.Current => Current;

            public void Dispose()
            {
                _buffer?.Dispose();
                _reader?.Dispose();
            }
        }

        private const int LengthMask = ~(31 << 26);
        private static int CombineKeyLength(byte k, int l) => (k << 26) | (l & LengthMask);
        private static byte GetKey(int kl) => (byte)((uint)kl >> 26);
        private static int GetLength(int kl) => kl & LengthMask;

        public static ImmutableList<RemoteInstrument> Create(ExtendedActorSystem system, ILoggingAdapter log)
        {
            var config = system.Settings.Config.GetConfig("akka.remote.artery.advanced");
            if (config.IsNullOrEmpty())
                throw Akka.Configuration.ConfigurationException.NullOrEmptyConfig<RemoteInstruments>();

            var typeList = config.GetStringList("instruments");
            var remoteInstrumentType = typeof(RemoteInstrument);
            var result = new List<RemoteInstrument>();
            foreach (var typeName in typeList)
            {
                RemoteInstrument instrument;
                var type = Type.GetType(typeName);
                if (!remoteInstrumentType.IsAssignableFrom(type))
                    throw new IllegalArgumentException($"Class type {type} does not inherit {nameof(RemoteInstrument)} abstract class.");
                try
                {
                    instrument = (RemoteInstrument)Activator.CreateInstance(type);
                }
                catch
                {
                    instrument = (RemoteInstrument)Activator.CreateInstance(type, system);
                }
                result.Add(instrument);
            }

            return result.ToImmutableList();
        }
    }
}
