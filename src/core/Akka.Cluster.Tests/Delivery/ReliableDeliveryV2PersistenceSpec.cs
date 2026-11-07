// -----------------------------------------------------------------------
//  <copyright file="ReliableDeliveryV2PersistenceSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

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
using Akka.Cluster.Configuration;
using Akka.Configuration;
using Akka.Delivery;
using Akka.Persistence;
using Akka.Persistence.Delivery;
using Akka.Persistence.Journal;
using Akka.TestKit;
using Google.Protobuf;
using Xunit;
using ActorTestKit = Akka.TestKit.Xunit.TestKit;
using State = Akka.Delivery.DurableProducerQueue.State<string>;

namespace Akka.Cluster.Tests.Delivery;

public sealed class ReliableDeliveryV2PersistenceSpec : AkkaSpec
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly ITestOutputHelper _output;

    public ReliableDeliveryV2PersistenceSpec(ITestOutputHelper output) : base(output)
    {
        _output = output;
    }

    [Fact(DisplayName = "Should_RecoverBothJournalFormats_When_SerializationV2IsEnabledThenDisabledAcrossSystems")]
    public Task Should_RecoverBothJournalFormats_When_SerializationV2IsEnabledThenDisabledAcrossSystems()
        => VerifyRecovery(useSnapshots: false);

    [Fact(DisplayName = "Should_RecoverLocalSnapshots_When_SerializationV2IsEnabledThenDisabledAcrossSystems")]
    public Task Should_RecoverLocalSnapshots_When_SerializationV2IsEnabledThenDisabledAcrossSystems()
        => VerifyRecovery(useSnapshots: true);

    private async Task VerifyRecovery(bool useSnapshots)
    {
        var directory = Path.Combine(Path.GetTempPath(), "akka-delivery-v2-" + Guid.NewGuid().ToString("N"));
        var persistenceId = "delivery-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(directory);
        try
        {
            await WithSystem(false, async (kit, queue) =>
            {
                AssertState(await LoadState(kit, queue), 1, 0);
                await Store(kit, queue, 1, "legacy");
                AssertState(await LoadState(kit, queue), 2, 0, "legacy");
                if (useSnapshots)
                    await SnapshotCommitted(kit, 1, 36);
            });

            var historical = FileJournal.ReadRecords(directory, persistenceId).Single();
            Assert.Equal(36, historical.SerializerId);
            Assert.Equal("f", historical.Manifest);

            await WithSystem(true, async (kit, queue) =>
            {
                AssertState(await LoadState(kit, queue), 2, 0, "legacy");
                await Store(kit, queue, 2, "v2");
                queue.Tell(new DurableProducerQueue.StoreMessageConfirmed(1, "", 1234));
                AssertState(await LoadState(kit, queue), 3, 1, "v2");
                if (useSnapshots)
                    await SnapshotCommitted(kit, 3, 76);
            });

            // The original on-disk payload is retained unchanged, including after another system writes V2.
            Assert.Equal(historical.Bytes, FileJournal.ReadRecords(directory, persistenceId).First().Bytes);

            await WithSystem(false, async (kit, queue) =>
            {
                AssertState(await LoadState(kit, queue), 3, 1, "v2");
                await Store(kit, queue, 3, "legacy-again");
                AssertState(await LoadState(kit, queue), 4, 1, "v2", "legacy-again");
                if (useSnapshots)
                    await SnapshotCommitted(kit, 4, 36);
            });

            var records = FileJournal.ReadRecords(directory, persistenceId);
            Assert.Equal(new[] { 36, 76, 76, 36 }, records.Select(e => e.SerializerId));
            Assert.Equal(new[] { "f", "f", "g", "f" }, records.Select(e => e.Manifest));

            if (useSnapshots)
            {
                var snapshots = Directory.GetFiles(Path.Combine(directory, "snapshots"), "snapshot-*");
                Assert.Contains(snapshots, path => ReadSnapshotPayload(path) == (36, "h"));
                Assert.Contains(snapshots, path => ReadSnapshotPayload(path) == (76, "h"));
            }

            async Task WithSystem(bool enabled, Func<ActorTestKit, IActorRef, Task> action)
            {
                var config = ConfigurationFactory.ParseString($$"""
                    akka.actor.serialization-v2 = {{(enabled ? "on" : "off")}}
                    akka.persistence.journal.plugin = delivery-file-journal
                    akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.local"
                    akka.persistence.snapshot-store.local.dir = "{{Path.Combine(directory, "snapshots").Replace('\\', '/')}}"
                    delivery-file-journal {
                        class = "{{typeof(FileJournal).AssemblyQualifiedName}}"
                        plugin-dispatcher = "akka.actor.default-dispatcher"
                        dir = "{{directory.Replace('\\', '/')}}"
                    }
                    """).WithFallback(global::Akka.Persistence.Persistence.DefaultConfig())
                    .WithFallback(ClusterConfigFactory.Default());

                var kit = new ActorTestKit(config, "delivery-format-recovery", _output);
                try
                {
                    var settings = EventSourcedProducerQueue.Settings.Create(kit.Sys) with
                    {
                        SnapshotEvery = useSnapshots ? 1 : int.MaxValue,
                        KeepNSnapshots = 10,
                        DeleteEvents = useSnapshots,
                        CleanupUnusedAfter = TimeSpan.FromDays(3650)
                    };
                    var queue = kit.Sys.ActorOf(EventSourcedProducerQueue.Create<string>(persistenceId, settings), "queue");
                    await action(kit, queue);
                }
                finally
                {
                    try
                    {
                        await kit.ShutdownAsync(Timeout, verifySystemShutdown: true);
                    }
                    finally
                    {
                        await kit.DisposeAsync();
                    }
                }
            }

            async Task SnapshotCommitted(ActorTestKit kit, long sequenceNr, int serializerId)
            {
                await kit.AwaitAssertAsync(() =>
                {
                    var path = Directory.GetFiles(Path.Combine(directory, "snapshots"),
                        $"snapshot-{Uri.EscapeDataString(persistenceId)}-{sequenceNr}-*")
                        .Single(p => !p.EndsWith(".tmp", StringComparison.Ordinal));
                    Assert.Equal((serializerId, "h"), ReadSnapshotPayload(path));
                    // No journal event remains replayable. The next system must recover from the real snapshot.
                    Assert.Equal(sequenceNr, FileJournal.ReadDeletedTo(directory, persistenceId));
                }, Timeout, TimeSpan.FromMilliseconds(50));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task Store(ActorTestKit kit, IActorRef queue, long seqNr, string payload)
    {
        var probe = kit.CreateTestProbe();
        queue.Tell(new DurableProducerQueue.StoreMessageSent<string>(
            new DurableProducerQueue.MessageSent<string>(seqNr, payload, false, "", 1234), probe.Ref));
        var ack = await probe.ExpectMsgAsync<DurableProducerQueue.StoreMessageSentAck>(Timeout);
        Assert.Equal(seqNr, ack.StoredSeqNo);
    }

    private static async Task<State> LoadState(ActorTestKit kit, IActorRef queue)
    {
        var probe = kit.CreateTestProbe();
        queue.Tell(new DurableProducerQueue.LoadState(probe.Ref));
        return await probe.ExpectMsgAsync<State>(Timeout);
    }

    private static void AssertState(State state, long currentSeqNr, long confirmedSeqNr, params string[] pending)
    {
        Assert.Equal(currentSeqNr, state.CurrentSeqNr);
        Assert.Equal(confirmedSeqNr, state.HighestConfirmedSeqNr);
        Assert.Equal(pending, state.Unconfirmed.Select(m => m.Message.Message));
    }

    private static (int SerializerId, string Manifest) ReadSnapshotPayload(string path)
    {
        // LocalSnapshotStore writes the public PersistenceSnapshotSerializer's PersistentPayload envelope.
        // Read only its id/manifest fields without depending on Persistence's internal generated proto type.
        using var input = new CodedInputStream(File.ReadAllBytes(path));
        var serializerId = 0;
        var manifest = "";
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 8:
                    serializerId = input.ReadInt32();
                    break;
                case 26:
                    manifest = input.ReadBytes().ToStringUtf8();
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }
        return (serializerId, manifest);
    }

    internal sealed record StoredEvent(long SequenceNr, int SerializerId, string Manifest, byte[] Bytes,
        string PersistentManifest, string WriterGuid, long Timestamp);

    /// <summary>
    /// Test-only journal adapted from #8407's WireFormatJournal. Each atomic write is an atomically
    /// renamed file containing metadata and serialized payload bytes, never a retained message object.
    /// </summary>
    public sealed class FileJournal : AsyncWriteJournal
    {
        private readonly string _directory = Context.System.Settings.Config.GetString("delivery-file-journal.dir");
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

        internal static IReadOnlyList<StoredEvent> ReadRecords(string directory, string persistenceId)
        {
            var events = new List<StoredEvent>();
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
                    events.Add(new StoredEvent(seqNr, serializerId, manifest, bytes, persistentManifest, writerGuid, timestamp));
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
}
