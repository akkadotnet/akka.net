// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence;
using Akka.Persistence.Journal;

namespace Akka.Cluster.Sharding.Tests;

internal sealed record ShardingStoredEvent(long SequenceNr, int SerializerId, string Manifest, byte[] Bytes,
    string PersistentManifest, string WriterGuid, long Timestamp);

/// <summary>
/// Test-only journal adapted from #8407's WireFormatJournal. Each atomic write is an atomically
/// renamed file containing metadata and serialized payload bytes, never a retained message object.
/// </summary>
public sealed class ShardingV2FileJournal : AsyncWriteJournal
{
    private readonly string _directory = Context.System.Settings.Config.GetString("sharding-file-journal.dir");
    private readonly Akka.Serialization.Serialization _serialization = Context.System.Serialization;

    protected override Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages,
        CancellationToken cancellationToken)
    {
        foreach (var atomicWrite in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var events = ((IEnumerable<IPersistentRepresentation>)atomicWrite.Payload).ToArray();
            var path = Path.Combine(EventDirectory(_directory, events[0].PersistenceId),
                events[0].SequenceNr.ToString("D20", CultureInfo.InvariantCulture) + ".events");
            Commit(path, writer =>
            {
                writer.Write(events.Length);
                foreach (var persistent in events)
                {
                    var serializer = _serialization.FindSerializerFor(persistent.Payload);
                    var bytes = serializer.ToBinary(persistent.Payload);
                    writer.Write(persistent.SequenceNr);
                    writer.Write(serializer.Identifier);
                    writer.Write(Akka.Serialization.Serialization.ManifestFor(serializer, persistent.Payload));
                    writer.Write(persistent.Manifest);
                    writer.Write(persistent.WriterGuid);
                    writer.Write(persistent.Timestamp);
                    writer.Write(bytes.Length);
                    writer.Write(bytes);
                }
            });
        }
        return Task.FromResult<IImmutableList<Exception>>(null!);
    }

    public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadRecords(_directory, persistenceId).Select(e => e.SequenceNr).DefaultIfEmpty().Max());
    }

    public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr,
        long toSequenceNr, long max, Action<IPersistentRepresentation> recoveryCallback)
    {
        var deletedTo = ReadDeletedTo(_directory, persistenceId);
        foreach (var record in ReadRecords(_directory, persistenceId)
                     .Where(e => e.SequenceNr > deletedTo && e.SequenceNr >= fromSequenceNr && e.SequenceNr <= toSequenceNr)
                     .Take((int)Math.Min(max, int.MaxValue)))
        {
            var payload = _serialization.Deserialize(record.Bytes, record.SerializerId, record.Manifest);
            recoveryCallback(new Persistent(payload, record.SequenceNr, persistenceId, record.PersistentManifest,
                false, ActorRefs.NoSender, record.WriterGuid, record.Timestamp));
        }
        return Task.CompletedTask;
    }

    protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deletedTo = Math.Max(ReadDeletedTo(_directory, persistenceId), toSequenceNr);
        Commit(Path.Combine(EventDirectory(_directory, persistenceId), "deleted-to"), w => w.Write(deletedTo));
        return Task.CompletedTask;
    }

    internal static long ReadDeletedTo(string directory, string persistenceId)
    {
        var path = Path.Combine(EventDirectory(directory, persistenceId), "deleted-to");
        if (!File.Exists(path))
            return 0;
        using var reader = new BinaryReader(File.OpenRead(path));
        return reader.ReadInt64();
    }

    internal static IReadOnlyList<ShardingStoredEvent> ReadRecords(string directory, string persistenceId)
    {
        var events = new List<ShardingStoredEvent>();
        foreach (var path in Directory.GetFiles(EventDirectory(directory, persistenceId), "*.events"))
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            var count = reader.ReadInt32();
            for (var i = 0; i < count; i++)
            {
                var seqNr = reader.ReadInt64();
                var serializerId = reader.ReadInt32();
                var manifest = reader.ReadString();
                var persistentManifest = reader.ReadString();
                var writerGuid = reader.ReadString();
                var timestamp = reader.ReadInt64();
                var bytes = reader.ReadBytes(reader.ReadInt32());
                events.Add(new ShardingStoredEvent(seqNr, serializerId, manifest, bytes, persistentManifest, writerGuid, timestamp));
            }
        }
        return events.OrderBy(e => e.SequenceNr).ToArray();
    }

    private static string EventDirectory(string directory, string persistenceId)
    {
        var path = Path.Combine(directory, Uri.EscapeDataString(persistenceId));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Commit(string path, Action<BinaryWriter> write)
    {
        var temporary = path + ".tmp";
        using (var writer = new BinaryWriter(File.Create(temporary)))
            write(writer);
        File.Move(temporary, path, overwrite: true);
    }
}
