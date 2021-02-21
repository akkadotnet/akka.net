using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Akka.Actor;
using Akka.Event;
using Akka.Remote.Artery.Internal;
using Akka.Remote.Artery.Utils;
using Akka.Util.Internal;
using ConfigurationException = Akka.Configuration.ConfigurationException;

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
        /// MUST be &gt;= 1 and &lt; 32.
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

    internal class LoggingRemoteInstrument : RemoteInstrument
    {
        private readonly int _logFrameSizeExceeding;
        private readonly ILoggingAdapter _log;
        private readonly ConcurrentDictionary<Type, int> _maxPayloadBytes;

        public LoggingRemoteInstrument(ActorSystem system)
        {
            var settings = system
                .AsInstanceOf<ExtendedActorSystem>()
                .Provider
                .AsInstanceOf<RemoteActorRefProvider>()
                .Transport
                .AsInstanceOf<ArteryTransport>()
                .Settings;
            _logFrameSizeExceeding = settings.LogFrameSizeExceeding.Get;
            _log = Logging.GetLogger(system, this);
            _maxPayloadBytes = new ConcurrentDictionary<Type, int>();
        }

        public override byte Identifier => 1; // Cinnamon is using 0
        public override void RemoteWriteMetadata(IActorRef recipient, object message, IActorRef sender, ByteBuffer buffer)
        {
            // no-op
        }

        public override void RemoteMessageSent(IActorRef recipient, object message, IActorRef sender, int size, long time)
        {
            if (size >= _logFrameSizeExceeding)
            {
                var clazz = message switch
                {
                    IWrappedMessage x => x.Message.GetType(),
                    _ => message.GetType()
                };

                // 10% threshold until next log
                var newMax = (int)(size * 1.1);

                while (true)
                {
                    if (!_maxPayloadBytes.TryGetValue(clazz, out var max))
                    {
                        if (_maxPayloadBytes.TryAdd(clazz, newMax))
                            _log.Info($"Payload size for [{clazz.Name}] is [{size}] bytes. Sent to {recipient}");
                        else
                            continue;
                    }
                    else if (size > max)
                    {
                        if (_maxPayloadBytes.TryUpdate(clazz, newMax, max))
                            _log.Info(
                                $"New maximum payload size for [{clazz.Name}] is [{size}] bytes. Sent to {recipient}.");
                        else
                            continue;
                    }

                    break;
                }
            }
        }

        public override void RemoteReadMetadata(IActorRef recipient, object message, IActorRef sender, ByteBuffer buffer)
        {
            // no-op
        }

        public override void RemoteMessageReceived(IActorRef recipient, object message, IActorRef sender, int size, long time)
        {
            // no-op
        }
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
        #region Static region

        public static RemoteInstruments Apply(ExtendedActorSystem system)
            => new RemoteInstruments(system);
        
                // key/length of a metadata element are encoded within a single integer:
        // supports keys in the range of <0-31>
        private const int LengthMask = ~(31 << 26);
        private static int CombineKeyLength(byte k, int l) => ((int)k << 26) | (l & LengthMask);
        private static byte GetKey(int kl) => (byte)((uint)kl >> 26);
        private static int GetLength(int kl) => kl & LengthMask;

        public static ImmutableList<RemoteInstrument> Create(
            ExtendedActorSystem system, 
            ILoggingAdapter log) // not used
        {
            var c = system.Settings.Config;
            var path = "akka.remote.artery.advanced.instruments";

            var configuredInstruments = c
                .GetStringList(path)
                .Select(fqcn =>
                {
                    var type = Type.GetType(fqcn);
                    if(type == null)
                        throw new ConfigurationException($"[akka.remote.artery.advanced.instruments] Failed to create instance of class [{fqcn}]");
                    try
                    {
                        return (RemoteInstrument)Activator.CreateInstance(type);
                    }
                    catch
                    {
                        return (RemoteInstrument)Activator.CreateInstance(type, BindingFlags.CreateInstance, null, new[] { system });
                    }
                }).ToList();

            return system.Provider switch
            {
                RemoteActorRefProvider rarp => rarp.Transport switch
                {
                    ArteryTransport artery => artery.Settings.LogFrameSizeExceeding switch
                    {
                        Some<int> _ => Add(configuredInstruments, new LoggingRemoteInstrument(system)).ToImmutableList(),
                        _ => configuredInstruments.ToImmutableList()
                    },
                    _ => configuredInstruments.ToImmutableList(),
                },
                _ => configuredInstruments.ToImmutableList(),
            };
        }

        #endregion

        private readonly ExtendedActorSystem _system;
        private readonly ILoggingAdapter _log;

        // keep the remote instruments sorted by identifier to speed up deserialization
        private readonly ImmutableList<RemoteInstrument> _instruments;
        // does any of the instruments want serialization timing?
        private readonly bool _serializationTimingEnabled;

        public bool IsEmpty => _instruments.Count == 0;
        public bool NonEmpty => _instruments.Count != 0;
        public bool TimeSerialization => _serializationTimingEnabled;

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
            _serializationTimingEnabled = _instruments.Exists(ri => ri.SerializationTimingEnabled);
        }

        public void Serialize(IOptionVal<IOutboundEnvelope> outboundEnvelope, ByteBuffer buffer)
        {
            if (_instruments.Count > 0 && outboundEnvelope.IsDefined)
            {
                var startPos = buffer.Position();
                var oe = outboundEnvelope.Get;
                try
                {
                    buffer.PutInt(0);
                    var dataPos = buffer.Position();
                    var i = 0;
                    while (i < _instruments.Count)
                    {
                        var rewindPos = buffer.Position();
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
                            buffer.Position(rewindPos);
                        }

                        ++i;
                    }

                    var endPos = buffer.Position();
                    if (endPos == dataPos)
                    {
                        // no instruments wrote anything so we need to rewind to start
                        buffer.Position(startPos);
                    }
                    else
                    {
                        // some instruments wrote data, so write the total length
                        buffer.PutInt(startPos, (endPos - dataPos));
                    }
                }
                catch(Exception e)
                {
                    if (!e.NonFatal()) throw;
                    _log.Debug($"Skipping serialization of all RemoteInstruments due to unhandled failure {e.Message}");
                    buffer.Position(startPos);
                }
            }
        }

        private void SerializeInstrument(
            RemoteInstrument instrument, 
            IOutboundEnvelope outboundEnvelope,
            ByteBuffer buffer)
        {
            var startPos = buffer.Position();
            buffer.PutInt(0);
            var dataPos = buffer.Position();
            instrument.RemoteWriteMetadata(
                outboundEnvelope.Recipient.OrNull(),
                outboundEnvelope.Message,
                outboundEnvelope.Sender.OrNull(),
                buffer);
            var endPos = buffer.Position();
            if (endPos == dataPos)
            {
                // if the instrument didn't write anything, then rewind to the start
                buffer.Position(startPos);
            }
            else
            {
                // the instrument wrote something so we need to write the identifier and length
                buffer.PutInt(startPos, CombineKeyLength(instrument.Identifier, endPos - dataPos));
            }
        }

        public void Deserialize(IInboundEnvelope inboundEnvelope)
        {
            if (inboundEnvelope.Flag(EnvelopeBuffer.MetadataPresentFlag))
            {
                inboundEnvelope.EnvelopeBuffer.ByteBuffer.Position(EnvelopeBuffer.MetadataContainerAndLiteralSectionOffset);
                DeserializeRaw(inboundEnvelope);
            }
        }

        public void DeserializeRaw(IInboundEnvelope inboundEnvelope)
        {
            var buffer = inboundEnvelope.EnvelopeBuffer.ByteBuffer;
            var l = buffer.GetInt();
            var endPos = buffer.Position() + l;
            try
            {
                if (!_instruments.IsEmpty)
                {
                    var i = 0;
                    while (i < _instruments.Count && buffer.Position() < endPos)
                    {
                        var instrument = _instruments[i];
                        var startPos = buffer.Position();
                        var keyAndLength = buffer.GetInt();
                        var dataPos = buffer.Position();
                        var key = GetKey(keyAndLength);
                        var length = GetLength(keyAndLength);
                        var nextPos = dataPos + length;
                        var identifier = instrument.Identifier;
                        if (key == identifier)
                        {
                            try
                            {
                                DeserializeInstrument(instrument, inboundEnvelope, buffer);
                            }
                            catch (Exception e)
                            {
                                if (!e.NonFatal())
                                    throw;
                                _log.Debug($"Skipping deserialization of RemoteInstrument {instrument.Identifier} since it failed with {e.Message}");
                            }

                            i++;
                        }
                        else if (key > identifier)
                        {
                            // since instruments are sorted on both sides skip this local one and retry the serialized one
                            _log.Debug($"Skipping local RemoteInstrument {identifier} that has no matching data in the message");
                            nextPos = startPos;
                            i++;
                        }
                        else
                        {
                            // since instruments are sorted on both sides skip the serialized one and retry the local one
                            _log.Debug($"Skipping serialized data in message for RemoteInstrument {key} that has no local match");
                        }

                        buffer.Position(nextPos);
                    }
                }
                else
                {
                    if (_log.IsDebugEnabled)
                        _log.Debug(
                            $"Skipping serialized data in message for RemoteInstrument(s) [{string.Join(", ", new RemoteInstrumentIdEnumeratorRaw(buffer, endPos))}] that has no local match");
                }
            }
            catch (Exception e)
            {
                if (!e.NonFatal())
                    throw;
                _log.Debug(
                    $"Skipping further deserialization of remaining RemoteInstruments due to unhandled failure {e}");
            }
            finally
            {
                buffer.Position(endPos);
            }
        }

        private void DeserializeInstrument(
            RemoteInstrument instrument, 
            IInboundEnvelope inboundEnvelope,
            ByteBuffer buffer)
        {
            instrument.RemoteReadMetadata(
                inboundEnvelope.Recipient.OrNull(),
                inboundEnvelope.Message,
                inboundEnvelope.Sender.OrNull(),
                buffer);
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

        private void MessageSentInstrument(
            RemoteInstrument instrument, 
            IOutboundEnvelope outboundEnvelope, 
            int size, 
            long time)
        {
            instrument.RemoteMessageSent(
                outboundEnvelope.Recipient.OrNull(),
                outboundEnvelope.Message,
                outboundEnvelope.Sender.OrNull(),
                size,
                time);
        }

        public void MessageReceived(IInboundEnvelope inboundEnvelope, int size, long time)
        {
            foreach (var instrument in _instruments)
            {
                try
                {
                    MessageReceivedInstrument(instrument, inboundEnvelope, size, time);
                }
                catch (Exception e)
                {
                    if (!e.NonFatal()) throw;
                    _log.Debug($"Message received in RemoteInstrument {instrument.Identifier} failed with {e.Message}");
                }
            }
        }

        public void MessageReceivedInstrument(
            RemoteInstrument instrument, 
            IInboundEnvelope inboundEnvelope, 
            int size,
            long time)
        {
            instrument.RemoteMessageReceived(
                inboundEnvelope.Recipient.OrNull(),
                inboundEnvelope.Message,
                inboundEnvelope.Sender.OrNull(),
                size,
                time);
        }

        private class RemoteInstrumentIdEnumeratorRaw : IEnumerator<int>
        {
            private readonly ByteBuffer _buffer;
            private readonly int _startPos;
            private readonly int _endPos;

            public RemoteInstrumentIdEnumeratorRaw(ByteBuffer buffer, int endPos)
            {
                _buffer = buffer;
                _startPos = _buffer.Position();
                _endPos = endPos;

            }

            public bool MoveNext()
            {
                if (_buffer.Position() >= _endPos) 
                    return false;

                var keyAndLength = _buffer.GetInt();
                _buffer.Position(_buffer.Position() + GetLength(keyAndLength));
                Current = GetKey(keyAndLength);
                return true;
            }

            public void Reset()
            {
                _buffer.Position(_startPos);
                Current = 0;
            }

            public int Current { get; private set; }

            object IEnumerator.Current => Current;

            public void Dispose() { }
        }

        private static IEnumerable<RemoteInstrument> Add(
            ICollection<RemoteInstrument> list, 
            RemoteInstrument instrument)
        {
            list.Add(instrument);
            return list;
        }
    }
}
