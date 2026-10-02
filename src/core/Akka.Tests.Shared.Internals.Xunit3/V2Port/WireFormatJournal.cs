//-----------------------------------------------------------------------
// <copyright file="WireFormatJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;

// ReSharper disable once CheckNamespace
namespace Akka.Persistence.Journal;

/// <summary>
/// A test journal that stores what a real one stores: bytes. <c>akka.persistence.journal.inmem</c> keeps the live
/// event objects and never calls a serializer, so a persistence test on it proves nothing about a serializer's wire
/// format. This journal looks the serializer up the way journal plugins do - <c>FindSerializerFor</c>, then
/// <c>ToBinary</c> - keeps only serializer id, manifest and bytes, and rebuilds the event with
/// <c>Serialization.Deserialize(bytes, id, manifest)</c> on replay.
/// </summary>
/// <remarks>
/// <para>
/// Pair it with the real <c>akka.persistence.snapshot-store.local</c> store, which already serializes its payloads, to
/// cover events and snapshots. A payload the serializer rejects is reported as a rejected write, as a real
/// journal reports a serialization error.
/// </para>
/// <para>
/// Ask the journal what it holds with <see cref="GetStoredEvents"/> (or <see cref="GetStoredAsync"/>) to check which
/// serializer wrote an event, and its bytes.
/// </para>
/// </remarks>
public sealed class WireFormatJournal : AsyncWriteJournal
{
    /// <summary>The config path <see cref="Config"/> registers the journal under.</summary>
    public const string PluginPath = "akka.persistence.journal.wire-format-test";

    /// <summary>
    /// Config that makes this the system's journal. Put it in front of <c>AkkaSpec</c>'s own config:
    /// <c>WireFormatJournal.Config.WithFallback(myConfig)</c>.
    /// </summary>
    public static Config Config { get; } = ConfigurationFactory.ParseString($$"""
        akka.persistence.journal.plugin = "{{PluginPath}}"
        {{PluginPath}} {
            class = "Akka.Persistence.Journal.WireFormatJournal, Akka.Tests.Shared.Internals.Xunit3"
            plugin-dispatcher = "akka.actor.default-dispatcher"
        }
        """);

    /// <summary>Asks the journal for what it holds for one persistence id. Replies with <see cref="StoredEvents"/>.</summary>
    public sealed record GetStoredEvents(string PersistenceId);

    /// <summary>The reply to <see cref="GetStoredEvents"/>.</summary>
    public sealed record StoredEvents(string PersistenceId, IReadOnlyList<StoredEventInfo> Events);

    /// <summary>One stored event, as bytes.</summary>
    /// <param name="SequenceNr">The event's sequence number.</param>
    /// <param name="SerializerId">The id of the serializer that wrote it.</param>
    /// <param name="Manifest">The manifest it was written with.</param>
    /// <param name="Bytes">What the journal stored.</param>
    /// <param name="PayloadType">The type of the event that was written, for assertions only; the journal never stores it.</param>
    /// <param name="IsDeleted">True after a delete up to this sequence number.</param>
    public sealed record StoredEventInfo(
        long SequenceNr, int SerializerId, string Manifest, byte[] Bytes, string PayloadType, bool IsDeleted);

    private sealed class StoredEvent
    {
        public StoredEvent(
            long seqNr, string manifest, int serializerId, byte[] bytes, string payloadType, string writerGuid, long timestamp)
        {
            SeqNr = seqNr;
            Manifest = manifest;
            SerializerId = serializerId;
            Bytes = bytes;
            PayloadType = payloadType;
            WriterGuid = writerGuid;
            Timestamp = timestamp;
        }

        public long SeqNr { get; }
        public string Manifest { get; }
        public int SerializerId { get; }
        public byte[] Bytes { get; }
        public string PayloadType { get; }
        public string WriterGuid { get; }
        public long Timestamp { get; }
        public bool Deleted { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, List<StoredEvent>> _events = new();

    /// <summary>
    /// Asks the system's default journal what it stores for <paramref name="persistenceId"/>. Works when this journal is
    /// the default one, as <see cref="Config"/> makes it.
    /// </summary>
    public static async Task<IReadOnlyList<StoredEventInfo>> GetStoredAsync(
        ActorSystem system, string persistenceId, TimeSpan? timeout = null)
    {
        var journal = Persistence.Instance.Apply(system).JournalFor(null);
        var reply = await journal.Ask<StoredEvents>(new GetStoredEvents(persistenceId), timeout ?? TimeSpan.FromSeconds(10));
        return reply.Events;
    }

    /// <inheritdoc />
    protected override Task<IImmutableList<Exception>> WriteMessagesAsync(
        IEnumerable<AtomicWrite> messages, CancellationToken cancellationToken)
    {
        var serialization = Context.System.Serialization;
        var results = new List<Exception?>();

        foreach (var atomicWrite in messages)
        {
            try
            {
                // serialize the whole atomic write before storing any of it: all or none
                var serialized = new List<(IPersistentRepresentation Persistent, StoredEvent Stored)>();
                foreach (var persistent in (IEnumerable<IPersistentRepresentation>)atomicWrite.Payload)
                {
                    var serializer = serialization.FindSerializerFor(persistent.Payload);
                    var manifest = global::Akka.Serialization.Serialization.ManifestFor(serializer, persistent.Payload);
                    var bytes = serializer.ToBinary(persistent.Payload);
                    serialized.Add((persistent, new StoredEvent(
                        persistent.SequenceNr, manifest, serializer.Identifier, bytes,
                        persistent.Payload.GetType().FullName ?? persistent.Payload.GetType().Name,
                        persistent.WriterGuid, DateTime.UtcNow.Ticks)));
                }

                lock (_gate)
                {
                    foreach (var (persistent, stored) in serialized)
                    {
                        if (!_events.TryGetValue(persistent.PersistenceId, out var list))
                            _events[persistent.PersistenceId] = list = new List<StoredEvent>();
                        list.Add(stored);
                    }
                }

                results.Add(null);
            }
            catch (Exception e)
            {
                results.Add(e);
            }
        }

        // a null entry is a write that went through
        return Task.FromResult<IImmutableList<Exception>>(results.ToImmutableList()!);
    }

    /// <inheritdoc />
    public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_events.TryGetValue(persistenceId, out var list) || list.Count == 0)
                return Task.FromResult(0L);

            return Task.FromResult(list.Max(e => e.SeqNr));
        }
    }

    /// <inheritdoc />
    public override Task ReplayMessagesAsync(
        IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max,
        Action<IPersistentRepresentation> recoveryCallback)
    {
        List<StoredEvent> snapshot;
        lock (_gate)
        {
            if (!_events.TryGetValue(persistenceId, out var list))
                return Task.CompletedTask;

            snapshot = new List<StoredEvent>(list);
        }

        var replayed = snapshot
            .Where(e => !e.Deleted && e.SeqNr >= fromSequenceNr && e.SeqNr <= toSequenceNr)
            .OrderBy(e => e.SeqNr)
            .Take(max > int.MaxValue ? int.MaxValue : (int)max);

        foreach (var stored in replayed)
        {
            var payload = context.System.Serialization.Deserialize(stored.Bytes, stored.SerializerId, stored.Manifest);
            recoveryCallback(new Persistent(
                payload, stored.SeqNr, persistenceId, stored.Manifest, false, ActorRefs.NoSender, stored.WriterGuid, stored.Timestamp));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_events.TryGetValue(persistenceId, out var list))
            {
                foreach (var stored in list.Where(e => e.SeqNr <= toSequenceNr))
                    stored.Deleted = true;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override bool ReceivePluginInternal(object message)
    {
        if (message is not GetStoredEvents request)
            return false;

        IReadOnlyList<StoredEventInfo> info;
        lock (_gate)
        {
            info = _events.TryGetValue(request.PersistenceId, out var list)
                ? list.Select(e => new StoredEventInfo(e.SeqNr, e.SerializerId, e.Manifest, (byte[])e.Bytes.Clone(), e.PayloadType, e.Deleted)).ToList()
                : Array.Empty<StoredEventInfo>();
        }

        Sender.Tell(new StoredEvents(request.PersistenceId, info));
        return true;
    }
}
