//-----------------------------------------------------------------------
// <copyright file="LegacyManifestPluginSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.Persistence.Snapshot;
using Akka.Persistence.Tests.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Tests
{
    /// <summary>
    /// Regression for https://github.com/akkadotnet/akka.net/issues/8784. The journal and snapshot store below
    /// pick the stored manifest exactly the way Akka.Persistence.Sql 1.6.0-beta2 does (and many plugins compiled
    /// against 1.5). A <see cref="SerializerV2"/> that only accepts its own manifest - like every source-generated
    /// serializer - must survive write, restart, recovery, another write, and another recovery through them.
    /// </summary>
    public sealed class LegacyManifestPluginSpec : AkkaSpec
    {
        private const string PersistenceId = "legacy-manifest-pid";
        private readonly string _storeId;

        public LegacyManifestPluginSpec(ITestOutputHelper output)
            : this(Guid.NewGuid().ToString("N"), output)
        {
        }

        private LegacyManifestPluginSpec(string storeId, ITestOutputHelper output)
            : base(SpecConfig(storeId), output)
        {
            _storeId = storeId;
        }

        private static Config SpecConfig(string storeId) => ConfigurationFactory.ParseString($@"
            akka.actor {{
              serializers {{
                my-v2-payload = ""Akka.Persistence.Tests.Serialization.MyV2PayloadSerializer, Akka.Persistence.Tests""
              }}
              serialization-bindings {{
                ""Akka.Persistence.Tests.Serialization.MyV2Payload, Akka.Persistence.Tests"" = my-v2-payload
              }}
            }}
            akka.persistence {{
              journal {{
                plugin = ""akka.persistence.journal.legacy-manifest""
                legacy-manifest {{
                  class = ""Akka.Persistence.Tests.LegacyManifestJournal, Akka.Persistence.Tests""
                  store-id = ""{storeId}""
                }}
              }}
              snapshot-store {{
                plugin = ""akka.persistence.snapshot-store.legacy-manifest""
                legacy-manifest {{
                  class = ""Akka.Persistence.Tests.LegacyManifestSnapshotStore, Akka.Persistence.Tests""
                  store-id = ""{storeId}""
                }}
              }}
            }}");

        [Fact(DisplayName = "Should_store_V2_manifest_and_recover_across_restarts_When_plugin_uses_legacy_manifest_dispatch")]
        public async Task Should_store_V2_manifest_and_recover_across_restarts_When_plugin_uses_legacy_manifest_dispatch()
        {
            // Run 1: write two events, a snapshot, then a third event.
            await RunIncarnationAsync("run1", async (actor, probe) =>
            {
                await probe.ExpectMsgAsync("recovered:");
                actor.Tell("persist:a", probe);
                await probe.ExpectMsgAsync("ack:a");
                actor.Tell("persist:b", probe);
                await probe.ExpectMsgAsync("ack:b");
                actor.Tell("snap", probe);
                await probe.ExpectMsgAsync("snap-saved");
                actor.Tell("persist:c", probe);
                await probe.ExpectMsgAsync("ack:c");
            });

            // Run 2 (restart): recover snapshot + event c, then write another event. Before #8784 was fixed every
            // write above was ACKed, but the stored CLR type name manifest made this recovery fail.
            await RunIncarnationAsync("run2", async (actor, probe) =>
            {
                await probe.ExpectMsgAsync("recovered:a,b,c");
                actor.Tell("persist:d", probe);
                await probe.ExpectMsgAsync("ack:d");
            });

            // Run 3 (restart): recover everything written by both incarnations.
            await RunIncarnationAsync("run3", async (_, probe) =>
            {
                await probe.ExpectMsgAsync("recovered:a,b,c,d");
            });

            AssertStoredRows(expectedEvents: 4, expectedSnapshots: 1);
        }

        private async Task RunIncarnationAsync(string name, Func<IActorRef, TestProbe, Task> body)
        {
            var system = ActorSystem.Create($"{Sys.Name}-{name}", Sys.Settings.Config);
            try
            {
                var probe = CreateTestProbe(system);
                var actor = system.ActorOf(Props.Create(() => new LegacyManifestActor(PersistenceId, probe.Ref)), "legacy");
                await body(actor, probe);
            }
            finally
            {
                await ShutdownAsync(system, verifySystemShutdown: true);
            }
        }

        private void AssertStoredRows(int expectedEvents, int expectedSnapshots)
        {
            var store = LegacyManifestStore.Get(_storeId);

            var events = store.Events.ToArray();
            events.Should().HaveCount(expectedEvents);
            events.Should().OnlyContain(row =>
                row.SerializerId == 77124 && row.Manifest == MyV2PayloadSerializer.PayloadManifest);

            var snapshots = store.Snapshots.ToArray();
            snapshots.Should().HaveCount(expectedSnapshots);
            snapshots.Should().OnlyContain(row =>
                row.SerializerId == 77124 && row.Manifest == MyV2PayloadSerializer.PayloadManifest);
        }

        private sealed class LegacyManifestActor : UntypedPersistentActor
        {
            private readonly IActorRef _probe;
            private readonly List<string> _state = new();

            public LegacyManifestActor(string persistenceId, IActorRef probe)
            {
                PersistenceId = persistenceId;
                _probe = probe;
            }

            public override string PersistenceId { get; }

            protected override void OnRecover(object message)
            {
                switch (message)
                {
                    case SnapshotOffer { Snapshot: MyV2Payload snapshot }:
                        _state.Clear();
                        _state.AddRange(snapshot.Data.Split(',', StringSplitOptions.RemoveEmptyEntries));
                        break;
                    case MyV2Payload evt:
                        _state.Add(evt.Data);
                        break;
                    case RecoveryCompleted:
                        _probe.Tell("recovered:" + string.Join(",", _state));
                        break;
                }
            }

            protected override void OnCommand(object message)
            {
                switch (message)
                {
                    case string s when s.StartsWith("persist:", StringComparison.Ordinal):
                        var data = s.Substring("persist:".Length);
                        Persist(new MyV2Payload(data), evt =>
                        {
                            _state.Add(evt.Data);
                            Sender.Tell("ack:" + evt.Data);
                        });
                        break;
                    case "snap":
                        SaveSnapshot(new MyV2Payload(string.Join(",", _state)));
                        break;
                    case SaveSnapshotSuccess:
                        _probe.Tell("snap-saved");
                        break;
                    case SaveSnapshotFailure failure:
                        _probe.Tell("snap-failed:" + failure.Cause.Message);
                        break;
                }
            }

            protected override void OnRecoveryFailure(Exception reason, object? message = null)
            {
                _probe.Tell("recovery-failed:" + reason.Message);
                base.OnRecoveryFailure(reason, message);
            }
        }
    }

    /// <summary>
    /// The manifest selection used by Akka.Persistence.Sql 1.6.0-beta2's writers (journal and both snapshot
    /// serializers), Akka.Persistence.Redis, and many third-party plugins compiled against 1.5.
    /// </summary>
    internal static class LegacyManifestDispatch
    {
        public static string ManifestFor(Akka.Serialization.Serializer serializer, object payload) => serializer switch
        {
            SerializerWithStringManifest stringManifest => stringManifest.Manifest(payload),
            { IncludeManifest: true } => payload.GetType().TypeQualifiedName(),
            _ => string.Empty
        };

        public static StoredRow Write(Akka.Serialization.Serialization serialization, object payload)
        {
            var serializer = serialization.FindSerializerFor(payload);
            return new StoredRow(
                serializer.ToBinary(payload),
                serializer.Identifier,
                ManifestFor(serializer, payload));
        }

        public static object Read(Akka.Serialization.Serialization serialization, StoredRow row)
            => serialization.Deserialize(row.Bytes, row.SerializerId, row.Manifest);
    }

    internal sealed record StoredRow(byte[] Bytes, int SerializerId, string Manifest);

    internal sealed record JournalRow(
        string PersistenceId,
        long SequenceNr,
        string WriterGuid,
        byte[] Bytes,
        int SerializerId,
        string Manifest);

    internal sealed record SnapshotRow(
        string PersistenceId,
        long SequenceNr,
        DateTime Timestamp,
        byte[] Bytes,
        int SerializerId,
        string Manifest);

    /// <summary>
    /// Survives <see cref="ActorSystem"/> restarts, standing in for a database.
    /// </summary>
    internal sealed class LegacyManifestStore
    {
        private static readonly ConcurrentDictionary<string, LegacyManifestStore> Stores = new();

        public static LegacyManifestStore Get(string storeId) => Stores.GetOrAdd(storeId, _ => new LegacyManifestStore());

        public ConcurrentQueue<JournalRow> Events { get; } = new();

        public ConcurrentQueue<SnapshotRow> Snapshots { get; } = new();
    }

    internal sealed class LegacyManifestJournal : AsyncWriteJournal
    {
        private readonly LegacyManifestStore _store;
        private readonly Akka.Serialization.Serialization _serialization;

        public LegacyManifestJournal(Config config)
        {
            _store = LegacyManifestStore.Get(config.GetString("store-id"));
            _serialization = Context.System.Serialization;
        }

        protected override Task<IImmutableList<Exception?>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages, CancellationToken cancellationToken)
        {
            foreach (var write in messages)
            {
                foreach (var repr in (IEnumerable<IPersistentRepresentation>)write.Payload)
                {
                    var row = LegacyManifestDispatch.Write(_serialization, repr.Payload);
                    _store.Events.Enqueue(new JournalRow(
                        repr.PersistenceId, repr.SequenceNr, repr.WriterGuid, row.Bytes, row.SerializerId, row.Manifest));
                }
            }

            return Task.FromResult<IImmutableList<Exception?>>(null!);
        }

        public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> recoveryCallback)
        {
            var rows = _store.Events
                .Where(r => r.PersistenceId == persistenceId && r.SequenceNr >= fromSequenceNr && r.SequenceNr <= toSequenceNr)
                .OrderBy(r => r.SequenceNr)
                .Take(max > int.MaxValue ? int.MaxValue : (int)max);

            foreach (var r in rows)
            {
                var payload = LegacyManifestDispatch.Read(_serialization, new StoredRow(r.Bytes, r.SerializerId, r.Manifest));
                recoveryCallback(new Persistent(payload, r.SequenceNr, r.PersistenceId, string.Empty, false, ActorRefs.NoSender, r.WriterGuid));
            }

            return Task.CompletedTask;
        }

        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr, CancellationToken cancellationToken)
        {
            var highest = _store.Events
                .Where(r => r.PersistenceId == persistenceId)
                .Select(r => r.SequenceNr)
                .DefaultIfEmpty(0L)
                .Max();
            return Task.FromResult(highest);
        }

        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    internal sealed class LegacyManifestSnapshotStore : SnapshotStore
    {
        private readonly LegacyManifestStore _store;
        private readonly Akka.Serialization.Serialization _serialization;

        public LegacyManifestSnapshotStore(Config config)
        {
            _store = LegacyManifestStore.Get(config.GetString("store-id"));
            _serialization = Context.System.Serialization;
        }

        protected override Task<SelectedSnapshot?> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria, CancellationToken cancellationToken)
        {
            var row = _store.Snapshots
                .Where(r => r.PersistenceId == persistenceId
                            && r.SequenceNr <= criteria.MaxSequenceNr
                            && r.SequenceNr >= criteria.MinSequenceNr)
                .OrderByDescending(r => r.SequenceNr)
                .FirstOrDefault();

            if (row is null)
                return Task.FromResult<SelectedSnapshot?>(null);

            var snapshot = LegacyManifestDispatch.Read(_serialization, new StoredRow(row.Bytes, row.SerializerId, row.Manifest));
            return Task.FromResult<SelectedSnapshot?>(new SelectedSnapshot(
                new SnapshotMetadata(row.PersistenceId, row.SequenceNr, row.Timestamp), snapshot));
        }

        protected override Task SaveAsync(SnapshotMetadata metadata, object snapshot, CancellationToken cancellationToken)
        {
            var row = LegacyManifestDispatch.Write(_serialization, snapshot);
            _store.Snapshots.Enqueue(new SnapshotRow(
                metadata.PersistenceId, metadata.SequenceNr, metadata.Timestamp, row.Bytes, row.SerializerId, row.Manifest));
            return Task.CompletedTask;
        }

        protected override Task DeleteAsync(SnapshotMetadata metadata, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        protected override Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
