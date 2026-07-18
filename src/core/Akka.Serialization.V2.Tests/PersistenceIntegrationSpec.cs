//-----------------------------------------------------------------------
// <copyright file="PersistenceIntegrationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence;
using Akka.Persistence.Journal;
using Akka.Persistence.Snapshot;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Integration proof for openspec tasks 6.5 / 6.6 (and, together with the assertions below, task 8.6):
/// source-generated V2 events and snapshots must survive a real Akka.Persistence journal /
/// snapshot-store write-then-recover cycle, not just a direct <c>Serialization.cs</c> call.
///
/// <para>
/// The built-in <c>akka.persistence.journal.inmem</c> plugin (<see cref="MemoryJournal"/>) intentionally
/// stores the live <see cref="IPersistentRepresentation"/> object reference and never calls
/// <c>ToBinary</c>/<c>FromBinary</c> at all -- fine for actor-recovery-logic tests, but it would make an
/// event round-trip test through it a no-op that proves nothing about the generated serializer's wire
/// format. <see cref="WireFormatJournal"/> below is a small test-only journal that does what every real
/// journal plugin does: look up the bound Akka serializer for the payload (<c>FindSerializerFor</c>),
/// call its real <c>ToBinary</c>/<c>FromBinary</c>, and store only the resulting bytes + manifest +
/// serializer id. That is what actually exercises the generated <see cref="AkkaSerializer"/>.
/// </para>
///
/// <para>
/// Snapshots use the real, shipped <c>akka.persistence.snapshot-store.local</c> plugin
/// (<see cref="Akka.Persistence.Snapshot.LocalSnapshotStore"/>), which already round-trips its payload
/// through <c>Akka.Persistence.Serialization.PersistenceSnapshotSerializer</c> -&gt;
/// <c>system.Serialization.FindSerializerFor</c> -&gt; real bytes on disk, so no test-only substitute is
/// needed there.
/// </para>
/// </summary>
public class PersistenceIntegrationSpec : AkkaSpec
{
    private readonly string _snapshotDir = Path.Combine(Path.GetTempPath(), "akka-v2-persistence-integration-" + Guid.NewGuid().ToString("N"));

    private Config BuildConfig() => ConfigurationFactory.ParseString($@"
        akka.persistence.journal.plugin = ""akka.persistence.journal.wire-format-test""
        akka.persistence.journal.wire-format-test {{
            class = ""Akka.Serialization.V2.Tests.WireFormatJournal, Akka.Serialization.V2.Tests""
            plugin-dispatcher = ""akka.actor.default-dispatcher""
        }}
        akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.local""
        akka.persistence.snapshot-store.local.dir = ""{_snapshotDir.Replace("\\", "/")}""
        akka.actor {{
            serializers {{
                persistence-integration-test = ""Akka.Serialization.V2.Tests.PersistenceIntegrationSerializer, Akka.Serialization.V2.Tests""
            }}
            serialization-bindings {{
                ""Akka.Serialization.V2.Tests.IPersistenceIntegrationProtocol, Akka.Serialization.V2.Tests"" = persistence-integration-test
            }}
        }}");

    // Sys (from the AkkaSpec base) is deliberately left with default config and unused for actual
    // persistence work: each test spins up its own dedicated, per-test ActorSystem (BuildConfig()) so
    // the wire-format journal and the local snapshot-store directory are fully isolated per test.
    public PersistenceIntegrationSpec(ITestOutputHelper output) : base(output)
    {
    }

    [Fact(DisplayName = "Generated V2 event should persist and recover through a wire-format-serializing journal")]
    public async Task Generated_event_should_persist_and_recover_through_journal()
    {
        var system = ActorSystem.Create(Sys.Name + "-events", BuildConfig().WithFallback(AkkaSpecConfig));
        try
        {
            var probe = CreateTestProbe(system);
            var persistenceId = "order-" + Guid.NewGuid().ToString("N");

            var actor1 = system.ActorOf(OrderActor.Props(persistenceId, probe.Ref), "order-actor-1");
            actor1.Tell(new PersistedOrderPlaced(persistenceId, 5, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            var firstEcho = await probe.ExpectMsgAsync<PersistedOrderPlaced>(TimeSpan.FromSeconds(10));
            firstEcho.Quantity.Should().Be(5);

            actor1.Tell(new PersistedOrderPlaced(persistenceId, 3, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)));
            await probe.ExpectMsgAsync<PersistedOrderPlaced>(TimeSpan.FromSeconds(10));

            probe.Watch(actor1);
            actor1.Tell(PoisonPill.Instance);
            await probe.ExpectTerminatedAsync(actor1, TimeSpan.FromSeconds(10));

            // a brand new actor instance, backed by the SAME long-lived journal actor: recovery must
            // deserialize the events from the bytes the journal actually stored.
            var actor2 = system.ActorOf(OrderActor.Props(persistenceId, probe.Ref), "order-actor-2");
            actor2.Tell(GetState.Instance);
            var recovered = await probe.ExpectMsgAsync<PersistedOrderState>(TimeSpan.FromSeconds(10));

            recovered.Should().Be(new PersistedOrderState(persistenceId, 8));
        }
        finally
        {
            Shutdown(system);
        }
    }

    [Fact(DisplayName = "Generated V2 snapshot should save and load through the real local snapshot store")]
    public async Task Generated_snapshot_should_save_and_load_through_local_snapshot_store()
    {
        var system = ActorSystem.Create(Sys.Name + "-snapshots", BuildConfig().WithFallback(AkkaSpecConfig));
        try
        {
            var probe = CreateTestProbe(system);
            var persistenceId = "order-snap-" + Guid.NewGuid().ToString("N");

            var actor1 = system.ActorOf(OrderActor.Props(persistenceId, probe.Ref), "order-snap-actor-1");
            actor1.Tell(new PersistedOrderPlaced(persistenceId, 7, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            await probe.ExpectMsgAsync<PersistedOrderPlaced>(TimeSpan.FromSeconds(10));

            actor1.Tell(TakeSnapshot.Instance);
            var success = await probe.ExpectMsgAsync<SaveSnapshotSuccess>(TimeSpan.FromSeconds(10));
            success.Metadata.PersistenceId.Should().Be(persistenceId);

            probe.Watch(actor1);
            actor1.Tell(PoisonPill.Instance);
            await probe.ExpectTerminatedAsync(actor1, TimeSpan.FromSeconds(10));

            // fresh instance recovers purely from the on-disk snapshot bytes (no journal events at all
            // for this persistence id), proving LocalSnapshotStore really round-tripped the V2 payload
            // through PersistenceSnapshotSerializer -> our generated MessagePack serializer.
            var actor2 = system.ActorOf(OrderActor.Props(persistenceId, probe.Ref), "order-snap-actor-2");
            actor2.Tell(GetState.Instance);
            var recovered = await probe.ExpectMsgAsync<PersistedOrderState>(TimeSpan.FromSeconds(10));

            recovered.Should().Be(new PersistedOrderState(persistenceId, 7));
        }
        finally
        {
            Shutdown(system);
        }
    }

    protected override void AfterAll()
    {
        base.AfterAll();
        try
        {
            if (Directory.Exists(_snapshotDir))
                Directory.Delete(_snapshotDir, true);
        }
        catch (IOException)
        {
            // best effort cleanup
        }
    }
}

internal sealed class GetState
{
    public static readonly GetState Instance = new();
    private GetState() { }
}

internal sealed class TakeSnapshot
{
    public static readonly TakeSnapshot Instance = new();
    private TakeSnapshot() { }
}

internal sealed class OrderActor : ReceivePersistentActor
{
    public static Props Props(string persistenceId, IActorRef probe) =>
        Akka.Actor.Props.Create(() => new OrderActor(persistenceId, probe));

    public override string PersistenceId { get; }

    private readonly IActorRef _probe;
    private PersistedOrderState _state;

    public OrderActor(string persistenceId, IActorRef probe)
    {
        PersistenceId = persistenceId;
        _probe = probe;
        _state = new PersistedOrderState(persistenceId, 0);

        Command<PersistedOrderPlaced>(cmd => Persist(cmd, evt =>
        {
            _state = _state with { TotalQuantity = _state.TotalQuantity + evt.Quantity };
            _probe.Tell(evt);
        }));

        Command<TakeSnapshot>(_ => SaveSnapshot(_state));
        Command<SaveSnapshotSuccess>(msg => _probe.Tell(msg));
        Command<SaveSnapshotFailure>(msg => _probe.Tell(msg));
        Command<GetState>(_ => _probe.Tell(_state));

        Recover<PersistedOrderPlaced>(evt => _state = _state with { TotalQuantity = _state.TotalQuantity + evt.Quantity });
        Recover<SnapshotOffer>(offer => _state = (PersistedOrderState)offer.Snapshot);
        Recover<RecoveryCompleted>(_ => { });
    }
}

public interface IPersistenceIntegrationProtocol
{
}

[AkkaSerializer<IPersistenceIntegrationProtocol>("persistence-integration-test", 120320)]
public sealed partial class PersistenceIntegrationSerializer : AkkaSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}

[AkkaSerializable(Manifest = "persisted-order-placed-v1")]
public sealed record PersistedOrderPlaced(
    [property: AkkaField(1)] string OrderId,
    [property: AkkaField(2)] int Quantity,
    [property: AkkaField(3)] DateTime PlacedAt) : IPersistenceIntegrationProtocol;

[AkkaSerializable(Manifest = "persisted-order-state-v1")]
public sealed record PersistedOrderState(
    [property: AkkaField(1)] string OrderId,
    [property: AkkaField(2)] int TotalQuantity) : IPersistenceIntegrationProtocol;

/// <summary>
/// Test-only journal that stores each event as real serialized bytes (serializer id + manifest +
/// payload), obtained via the actor system's normal <c>Serialization.FindSerializerFor</c> /
/// <c>ToBinary</c> / <c>Deserialize</c> path -- see the doc comment on <see cref="PersistenceIntegrationSpec"/>
/// for why the built-in in-memory journal can't be used for this proof.
/// </summary>
public sealed class WireFormatJournal : AsyncWriteJournal
{
    private sealed class StoredEvent
    {
        public StoredEvent(long seqNr, string manifest, int serializerId, byte[] bytes, string writerGuid, long timestamp)
        {
            SeqNr = seqNr;
            Manifest = manifest;
            SerializerId = serializerId;
            Bytes = bytes;
            WriterGuid = writerGuid;
            Timestamp = timestamp;
        }

        public long SeqNr { get; }
        public string Manifest { get; }
        public int SerializerId { get; }
        public byte[] Bytes { get; }
        public string WriterGuid { get; }
        public long Timestamp { get; }
        public bool Deleted { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, List<StoredEvent>> _events = new();

    protected override Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            foreach (var atomicWrite in messages)
            {
                foreach (var persistent in (IEnumerable<IPersistentRepresentation>)atomicWrite.Payload)
                {
                    var serializer = Context.System.Serialization.FindSerializerFor(persistent.Payload);
                    var manifest = global::Akka.Serialization.Serialization.ManifestFor(serializer, persistent.Payload);
                    var bytes = serializer.ToBinary(persistent.Payload);

                    if (!_events.TryGetValue(persistent.PersistenceId, out var list))
                    {
                        list = new List<StoredEvent>();
                        _events[persistent.PersistenceId] = list;
                    }

                    list.Add(new StoredEvent(persistent.SequenceNr, manifest, serializer.Identifier, bytes, persistent.WriterGuid, DateTime.UtcNow.Ticks));
                }
            }
        }

        return Task.FromResult<IImmutableList<Exception>>(null!);
    }

    public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_events.TryGetValue(persistenceId, out var list) || list.Count == 0)
                return Task.FromResult(0L);

            return Task.FromResult(list[list.Count - 1].SeqNr);
        }
    }

    public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max,
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
            .Take(max > int.MaxValue ? int.MaxValue : (int)max);

        foreach (var stored in replayed)
        {
            var payload = Context.System.Serialization.Deserialize(stored.Bytes, stored.SerializerId, stored.Manifest);
            recoveryCallback(new Persistent(payload, stored.SeqNr, persistenceId, stored.Manifest, false, ActorRefs.NoSender, stored.WriterGuid, stored.Timestamp));
        }

        return Task.CompletedTask;
    }

    protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_events.TryGetValue(persistenceId, out var list))
            {
                foreach (var stored in list.Where(stored => stored.SeqNr <= toSequenceNr))
                    stored.Deleted = true;
            }
        }

        return Task.CompletedTask;
    }
}
