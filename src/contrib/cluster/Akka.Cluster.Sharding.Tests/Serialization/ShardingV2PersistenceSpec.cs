// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Sharding.Internal;
using Akka.Cluster.Configuration;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Google.Protobuf;
using Xunit;
using ActorTestKit = Akka.TestKit.Xunit.TestKit;

namespace Akka.Cluster.Sharding.Tests;

public sealed class ShardingV2PersistenceSpec : AkkaSpec
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly ITestOutputHelper _output;
    public ShardingV2PersistenceSpec(ITestOutputHelper output) : base(output)
    {
        _output = output;
    }

    [Fact(DisplayName = "Should_RecoverStoredEntityEvents_When_V2IsEnabledThenDisabledAcrossSystems")]
    public Task Should_RecoverStoredEntityEvents_When_V2IsEnabledThenDisabledAcrossSystems() => VerifyRecovery(false);

    [Fact(DisplayName = "Should_RecoverRealEntitySnapshots_When_V2IsEnabledThenDisabledAcrossSystems")]
    public Task Should_RecoverRealEntitySnapshots_When_V2IsEnabledThenDisabledAcrossSystems() => VerifyRecovery(true);

    private async Task VerifyRecovery(bool snapshots)
    {
        var directory = Path.Combine(Path.GetTempPath(), "akka-sharding-v2-" + Guid.NewGuid().ToString("N"));
        var typeName = "entities-" + Guid.NewGuid().ToString("N");
        var persistenceId = $"/sharding/{typeName}Shard/s1";
        Directory.CreateDirectory(directory);
        try
        {
            await WithSystem(false, async (kit, store) =>
            {
                await AssertEntities(kit, store);
                await Update(kit, store, ["legacy"], []);
                await AssertEntities(kit, store, "legacy");
                if (snapshots) await SnapshotCommitted(kit, 1, 13);
            });
            var historical = ShardingV2FileJournal.ReadRecords(directory, persistenceId).Single();
            Assert.Equal(13, historical.SerializerId);
            Assert.Equal("CE", historical.Manifest);

            await WithSystem(true, async (kit, store) =>
            {
                await AssertEntities(kit, store, "legacy");
                await Update(kit, store, ["v2"], ["legacy"]);
                await AssertEntities(kit, store, "v2");
                if (snapshots) await SnapshotCommitted(kit, 3, 53);
            });
            Assert.Equal(historical.Bytes, ShardingV2FileJournal.ReadRecords(directory, persistenceId).First().Bytes);

            await WithSystem(false, async (kit, store) =>
            {
                await AssertEntities(kit, store, "v2");
                await Update(kit, store, ["legacy-again"], []);
                await AssertEntities(kit, store, "v2", "legacy-again");
                if (snapshots) await SnapshotCommitted(kit, 4, 13);
            });
            var records = ShardingV2FileJournal.ReadRecords(directory, persistenceId);
            Assert.Equal(new[] { 13, 53, 53, 13 }, records.Select(record => record.SerializerId));
            Assert.Equal(new[] { "CE", "CE", "CF", "CE" }, records.Select(record => record.Manifest));

            async Task WithSystem(bool enabled, Func<ActorTestKit, IActorRef, Task> action)
            {
                var config = ConfigurationFactory.ParseString($$"""
                    akka.actor.provider = local
                    akka.actor.serialization-v2 = {{(enabled ? "on" : "off")}}
                    akka.persistence.journal.plugin = sharding-file-journal
                    akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.local"
                    akka.persistence.snapshot-store.local.dir = "{{Path.Combine(directory, "snapshots").Replace(Path.DirectorySeparatorChar, '/')}}"
                    akka.cluster.sharding {
                        journal-plugin-id = sharding-file-journal
                        snapshot-plugin-id = "akka.persistence.snapshot-store.local"
                        snapshot-after = {{(snapshots ? 1 : int.MaxValue)}}
                        keep-nr-of-batches = 0
                    }
                    sharding-file-journal {
                        class = "{{typeof(ShardingV2FileJournal).AssemblyQualifiedName}}"
                        plugin-dispatcher = "akka.actor.default-dispatcher"
                        dir = "{{directory.Replace(Path.DirectorySeparatorChar, '/')}}"
                    }
                    """).WithFallback(ClusterSharding.DefaultConfig())
                    .WithFallback(ClusterSingleton.DefaultConfig()).WithFallback(ClusterConfigFactory.Default())
                    .WithFallback(global::Akka.Persistence.Persistence.DefaultConfig());
                var kit = new ActorTestKit(config, "sharding-format-recovery", _output);
                try
                {
                    var settings = ClusterShardingSettings.Create(kit.Sys);
                    var store = kit.Sys.ActorOf(EventSourcedRememberEntitiesShardStore.Props(typeName, "s1", settings), "store");
                    await action(kit, store);
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

            async Task SnapshotCommitted(ActorTestKit kit, long sequenceNr, int id)
            {
                await kit.AwaitAssertAsync(() =>
                {
                    var path = Directory.GetFiles(Path.Combine(directory, "snapshots"),
                        $"snapshot-{Uri.EscapeDataString(persistenceId)}-{sequenceNr}-*")
                        .Single(path => !path.EndsWith(".tmp", StringComparison.Ordinal));
                    Assert.Equal((id, "CA"), ReadSnapshotPayload(path));
                    // Every event through this point is tombstoned: recovery must read the snapshot.
                    Assert.Equal(sequenceNr, ShardingV2FileJournal.ReadDeletedTo(directory, persistenceId));
                }, Timeout, TimeSpan.FromMilliseconds(50));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task Update(ActorTestKit kit, IActorRef store, string[] started, string[] stopped)
    {
        var probe = kit.CreateTestProbe();
        store.Tell(new RememberEntitiesShardStore.Update(started.ToImmutableHashSet(), stopped.ToImmutableHashSet()), probe.Ref);
        var ack = await probe.ExpectMsgAsync<RememberEntitiesShardStore.UpdateDone>(Timeout);
        Assert.True(ack.Started.SetEquals(started));
        Assert.True(ack.Stopped.SetEquals(stopped));
    }

    private static async Task AssertEntities(ActorTestKit kit, IActorRef store, params string[] expected)
    {
        var probe = kit.CreateTestProbe();
        store.Tell(RememberEntitiesShardStore.GetEntities.Instance, probe.Ref);
        var entities = await probe.ExpectMsgAsync<RememberEntitiesShardStore.RememberedEntities>(Timeout);
        Assert.True(entities.Entities.SetEquals(expected));
    }

    private static (int Id, string Manifest) ReadSnapshotPayload(string path)
    {
        // LocalSnapshotStore's existing PersistenceSnapshotSerializer stores a PersistentPayload.
        // Inspect its id/manifest without referencing Persistence's internal generated protobuf type.
        using var input = new CodedInputStream(File.ReadAllBytes(path));
        var id = 0;
        var manifest = "";
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 8: id = input.ReadInt32(); break;
                case 26: manifest = input.ReadBytes().ToStringUtf8(); break;
                default: input.SkipLastField(); break;
            }
        }
        return (id, manifest);
    }
}
