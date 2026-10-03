//-----------------------------------------------------------------------
// <copyright file="RowCodec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Persistence.Journal;
using Akka.Persistence.Query;
using Akka.Serialization;

namespace Akka.Persistence.Embedded.Internal
{
    /// <summary>A journal row ready to insert.</summary>
    internal sealed class JournalRow
    {
        public required long Created { get; init; }
        public required string PersistenceId { get; init; }
        public required long SequenceNr { get; init; }
        public required byte[] Message { get; init; }
        public required string Manifest { get; init; }
        public required int Identifier { get; init; }

        /// <summary>Csv tags column value. Empty string when untagged. Only written in Csv and Both modes.</summary>
        public required string TagsColumn { get; init; }

        /// <summary>One entry per tag, for the tag table.</summary>
        public required string[] Tags { get; init; }

        public required string? WriterUuid { get; init; }
    }

    /// <summary>A journal row as read from SQLite, before deserialization.</summary>
    internal sealed class RawJournalRow
    {
        public long Ordering { get; init; }
        public long Created { get; init; }
        public long Deleted { get; init; }
        public string PersistenceId { get; init; } = "";
        public long SequenceNr { get; init; }
        public byte[] Message { get; init; } = Array.Empty<byte>();
        public string? Manifest { get; init; }
        public long? Identifier { get; init; }
        public string? WriterUuid { get; init; }

        /// <summary>The tags column (Csv read) or the group_concat of the tag table (TagTable read). Null for replay.</summary>
        public string? TagList { get; init; }
    }

    /// <summary>Turns events into rows and rows back into events. Pure CPU work: never touches the database.</summary>
    internal sealed class RowCodec
    {
        private static readonly char[] TagTableSeparator = ['\u001f'];

        private readonly ExtendedActorSystem _system;
        private readonly Akka.Serialization.Serialization _serialization;
        private readonly JournalSettings _settings;
        private readonly string[] _csvSeparator;

        public RowCodec(ExtendedActorSystem system, JournalSettings settings)
        {
            _system = system;
            _serialization = system.Serialization;
            _settings = settings;
            _csvSeparator = [settings.TagSeparator];
        }

        public Akka.Serialization.Serialization Serialization => _serialization;

        /// <summary>Serializes one persistent representation. Throws when the payload cannot be serialized.</summary>
        public JournalRow Serialize(IPersistentRepresentation representation, long batchTicks, string? writerUuid)
        {
            var payload = representation.Payload;
            var tags = ImmutableHashSet<string>.Empty as IImmutableSet<string>;
            if (payload is Tagged tagged)
            {
                payload = tagged.Payload;
                tags = tagged.Tags ?? tags;
            }

            var (bytes, manifest, identifier) = SerializePayload(payload, _settings.DefaultSerializer);
            var tagArray = tags.ToArray();
            return new JournalRow
            {
                Created = representation.Timestamp == 0 ? batchTicks : representation.Timestamp,
                PersistenceId = representation.PersistenceId,
                SequenceNr = representation.SequenceNr,
                Message = bytes,
                Manifest = manifest,
                Identifier = identifier,
                TagsColumn = tagArray.Length == 0
                    ? string.Empty
                    : _settings.TagSeparator + string.Join(_settings.TagSeparator, tagArray) + _settings.TagSeparator,
                Tags = tagArray,
                WriterUuid = writerUuid
            };
        }

        /// <summary>Serializes one payload with transport information set, so actor refs inside it serialize.</summary>
        public (byte[] Bytes, string Manifest, int Identifier) SerializePayload(object? payload, string? defaultSerializerName)
            => SerializePayload(_system, payload, defaultSerializerName);

        /// <summary>Serializes one payload with transport information set, so actor refs inside it serialize.</summary>
        public static (byte[] Bytes, string Manifest, int Identifier) SerializePayload(
            ExtendedActorSystem system, object? payload, string? defaultSerializerName)
        {
            if (payload is null)
                throw new ArgumentNullException(nameof(payload), "Cannot persist a null payload.");

            var serializer = system.Serialization.FindSerializerForType(payload.GetType(), defaultSerializerName);
            var bytes = Akka.Serialization.Serialization.WithTransport(
                system, (serializer, payload), static s => s.serializer.ToBinary(s.payload));
            return (bytes, serializer.Manifest(payload) ?? string.Empty, serializer.Identifier);
        }

        /// <summary>Deserializes the payload of a journal row.</summary>
        public object DeserializePayload(RawJournalRow row, string identifierColumn)
        {
            if (row.Identifier is null)
            {
                throw new SerializationException(
                    $"Journal row ({row.PersistenceId}, {row.SequenceNr}) has a NULL {identifierColumn}. " +
                    "Akka.Persistence.Embedded cannot read rows without a serializer id (that needs Type.GetType). " +
                    "Re-write these rows with Akka.Persistence.Sql first.");
            }

            return _serialization.Deserialize(row.Message, (int)row.Identifier.Value, row.Manifest ?? string.Empty);
        }

        public IPersistentRepresentation ToPersistent(RawJournalRow row, string identifierColumn)
        {
            var payload = DeserializePayload(row, identifierColumn);
            return new Persistent(
                payload,
                row.SequenceNr,
                row.PersistenceId,
                row.Manifest ?? string.Empty,
                row.Deleted != 0,
                ActorRefs.NoSender,
                row.WriterUuid,
                row.Created);
        }

        /// <summary>Splits the tags of a row. <paramref name="fromTagTable"/> = the list came from group_concat(tag, char(31)).</summary>
        public string[] SplitTags(string? tagList, bool fromTagTable)
        {
            if (string.IsNullOrEmpty(tagList))
                return [];

            return fromTagTable
                ? tagList.Split(TagTableSeparator, StringSplitOptions.RemoveEmptyEntries)
                : tagList.Split(_csvSeparator, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>Builds the envelopes of a row: one per event the adapters produce. All share the row's offset.</summary>
        public static IEnumerable<EventEnvelope> ToEnvelopes(
            IPersistentRepresentation persistent, long ordering, string[] tags, EventAdapters adapters)
        {
            var payload = persistent.Payload;
            var events = adapters.Get(payload.GetType()).FromJournal(payload, persistent.Manifest).Events;
            foreach (var evt in events)
            {
                yield return new EventEnvelope(
                    Offset.Sequence(ordering), persistent.PersistenceId, persistent.SequenceNr, evt, persistent.Timestamp, tags);
            }
        }
    }
}
