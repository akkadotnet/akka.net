using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Persistence.Sql.Linq2Db.Config;
using Akka.Persistence.Sql.Linq2Db.Journal.Types;
using Akka.Persistence.Sql.Linq2Db.Serialization;
using Akka.Serialization;
using Akka.Streams.Dsl;
using Akka.Util;

namespace Akka.Persistence.Sql.Linq2Db.Journal.DAO
{
    /// <summary>
    /// Serializes <see cref="IPersistentRepresentation"/> 
    /// </summary>
    public class ByteArrayJournalSerializer : FlowPersistentReprSerializer<JournalRow>
    {
        private Akka.Serialization.Serialization _serializer;
        private string _separator;
        private IProviderConfig<JournalTableConfig> _journalConfig;

        public ByteArrayJournalSerializer(IProviderConfig<JournalTableConfig> journalConfig, Akka.Serialization.Serialization serializer, string separator)
        {
            _journalConfig = journalConfig;
            _serializer = serializer;
            _separator = separator;
        }
        protected override Try<JournalRow> Serialize(IPersistentRepresentation persistentRepr, IImmutableSet<string> tTags, long timeStamp = 0)
        {
            try
            {
                var serializer = _serializer.FindSerializerForType(persistentRepr.Payload.GetType(),_journalConfig.DefaultSerializer);
                // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
                string manifest = "";
                var binary = Akka.Serialization.Serialization.WithTransport(_serializer.System, () =>
                {
                
                    if (serializer is SerializerWithStringManifest stringManifest)
                    {
                        manifest =
                            stringManifest.Manifest(persistentRepr.Payload);
                    }
                    else
                    {
                        if (serializer.IncludeManifest)
                        {
                            manifest = persistentRepr.Payload.GetType().TypeQualifiedName();
                        }
                    }

                    return serializer.ToBinary(persistentRepr.Payload);
                });
                return new Try<JournalRow>(new JournalRow()
                {
                    manifest = manifest,
                    message = binary,
                    persistenceId = persistentRepr.PersistenceId,
                    tags = tTags.Any()?  tTags.Aggregate((tl, tr) => tl + _separator + tr) : "",
                    Identifier = serializer.Identifier,
                    sequenceNumber = persistentRepr.SequenceNr,
                    Timestamp = timeStamp
                });
            }
            catch (Exception e)
            {
                return new Try<JournalRow>(e);
            }
        }

        protected override Try<(IPersistentRepresentation, IImmutableSet<string>, long)> Deserialize(JournalRow t)
        {
            return Try<(IPersistentRepresentation, IImmutableSet<string>, long)>.From(
                () =>
                {
                    object deserialized = null;
                    if (t.Identifier.HasValue == false)
                    {
                        var type = System.Type.GetType(t.manifest, true);
                        var deserializer =
                            _serializer.FindSerializerForType(type, null);
                        // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
                        deserialized =
                            Akka.Serialization.Serialization.WithTransport(
                                _serializer.System,
                                () => deserializer.FromBinary(t.message, type));
                    }
                    else
                    {
                        var serializerId = t.Identifier.Value;
                        // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
                        deserialized = _serializer.Deserialize(t.message,
                            serializerId,t.manifest);
                    }

                    return (
                        new Persistent(deserialized, t.sequenceNumber,
                            t.persistenceId,
                            t.manifest, t.deleted, ActorRefs.NoSender, null),
                        t.tags?.Split(new[] {_separator},
                                StringSplitOptions.RemoveEmptyEntries)
                            .ToImmutableHashSet() ?? ImmutableHashSet<string>.Empty,
                        t.ordering);
                }
            );
        }
        public
            Flow<JournalRow, Try<ReplayCompletion>
                , NotUsed> DeserializeFlowCompletion()
        {
            return Flow.Create<JournalRow, NotUsed>().Select(DeserializeReplayCompletion);

        }
        public  Try<ReplayCompletion> DeserializeReplayCompletion(JournalRow t)
        {
            return Try<ReplayCompletion>.From(
                () =>
                {
                    object deserialized = null;
                    if (t.Identifier.HasValue == false)
                    {
                        var type = System.Type.GetType(t.manifest, true);
                        var deserializer =
                            _serializer.FindSerializerForType(type, null);
                        // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
                        deserialized =
                            Akka.Serialization.Serialization.WithTransport(
                                _serializer.System,
                                () => deserializer.FromBinary(t.message, type));
                    }
                    else
                    {
                        var serializerId = t.Identifier.Value;
                        // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
                        deserialized = _serializer.Deserialize(t.message,
                            serializerId,t.manifest);
                    }

                    return new ReplayCompletion()
                    {
                        repr =
                            new Persistent(deserialized, t.sequenceNumber,
                                t.persistenceId,
                                t.manifest, t.deleted, ActorRefs.NoSender,
                                null),
                        Ordering = t.ordering
                    };
                }
            );
        }
    }
}