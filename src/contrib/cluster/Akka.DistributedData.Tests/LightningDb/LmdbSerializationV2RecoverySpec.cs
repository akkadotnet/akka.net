// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.DistributedData.Durable;
using Akka.DistributedData.LightningDB;
using Akka.DistributedData.Serialization;
using Akka.DistributedData.Tests.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using LightningDB;
using Xunit;
using ActorTestKit = Akka.TestKit.Xunit.TestKit;

namespace Akka.DistributedData.Tests.LightningDb;

/// <summary>
/// LMDB has no record header. Built-in storage must retain id 12 while network messages use id 52;
/// existing custom durable bindings retain their own format. Every phase opens the same database
/// from a new actor system.
/// </summary>
public sealed class LmdbSerializationV2RecoverySpec : AkkaSpec
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly ITestOutputHelper _output;

    public LmdbSerializationV2RecoverySpec(ITestOutputHelper output) : base(output)
    {
        _output = output;
    }

    [Theory(DisplayName = "Should_RetainExistingStorageAndRecoverAllKeys_When_V2NetworkBindingsChangeAcrossSystems")]
    [InlineData("off", "module")]
    [InlineData("10ms", "module")]
    [InlineData("off", "type-pin")]
    [InlineData("10ms", "type-pin")]
    [InlineData("off", "legacy-alias")]
    [InlineData("10ms", "legacy-alias")]
    [InlineData("off", "crdt-module")]
    [InlineData("10ms", "crdt-module")]
    [InlineData("off", "crdt-typed")]
    [InlineData("10ms", "crdt-typed")]
    [InlineData("off", "crdt-legacy-alias")]
    [InlineData("10ms", "crdt-legacy-alias")]
    public async Task Should_RetainExistingStorageAndRecoverAllKeys_When_V2NetworkBindingsChangeAcrossSystems(string writeBehind, string binding)
    {
        var nestedCrdt = binding.StartsWith("crdt-", StringComparison.Ordinal);
        var customBinding = !nestedCrdt && binding != "module";
        var name = nestedCrdt ? CanonicalCrdtSetup.SystemName : "lmdb-v2-recovery";
        var directory = Path.Combine(Path.GetTempPath(), "akka-lmdb-v2-" + Guid.NewGuid().ToString("N"));
        var node = ReplicatorMessagePackSerializerSpec.Node(name, "counter.example", 1L << 40);
        var before = new DurableDataEnvelope(nestedCrdt ? CanonicalCrdtSetup.Register(7) : GCounter.Empty.Increment(node, 1));
        var after = new DurableDataEnvelope(nestedCrdt ? CanonicalCrdtSetup.Register(8) : GCounter.Empty.Increment(node, 2));
        Directory.CreateDirectory(directory);
        try
        {
            await WithStore(false, async (kit, store, loaded) =>
            {
                Assert.Empty(loaded);
                await Store(kit, store, "before", before);
            });
            var historical = ReadRecords(directory)["before"];

            await WithStore(true, async (kit, store, loaded) =>
            {
                Assert.Equal(binding == "type-pin" ? 777 : 52,
                    kit.Sys.Serialization.FindSerializerForType(typeof(DurableDataEnvelope)).Identifier);
                if (nestedCrdt)
                    Assert.Equal(51, kit.Sys.Serialization.FindSerializerFor(before.Data).Identifier);
                Assert.Equal(before, Assert.Single(loaded).Value);
                await Store(kit, store, "after", after);
            });
            var records = ReadRecords(directory);
            Assert.Equal(historical, records["before"]);
            Assert.Equal(2, records.Count);
            if (nestedCrdt)
            {
                foreach (var record in records.Values)
                    CanonicalCrdtSetup.AssertStoredCrdtIds(record, binding[5..]);
            }

            await WithStore(false, (kit, _, loaded) =>
            {
                Assert.Equal(customBinding ? 777 : 12, kit.Sys.Serialization.FindSerializerForType(typeof(DurableDataEnvelope)).Identifier);
                Assert.Equal(before, loaded["before"]);
                Assert.Equal(after, loaded["after"]);
                Serializer legacy = customBinding
                    ? new CustomDurableSerializer((ExtendedActorSystem)kit.Sys)
                    : new ReplicatorMessageSerializer((ExtendedActorSystem)kit.Sys);
                var manifest = customBinding ? "custom" : "P";
                // Read the actual headerless database bytes directly with the unchanged legacy reader.
                Assert.Equal(before, legacy.FromBinary(records["before"], manifest));
                Assert.Equal(after, legacy.FromBinary(records["after"], manifest));
                Assert.Equal(legacy.ToBinary(before), records["before"]);
                Assert.Equal(legacy.ToBinary(after), records["after"]);
                return Task.CompletedTask;
            });

            async Task WithStore(bool enabled,
                Func<ActorTestKit, IActorRef, ImmutableDictionary<string, DurableDataEnvelope>, Task> action)
            {
                var custom = binding == "legacy-alias" ? ConfigurationFactory.ParseString($$"""
                    akka.actor.serializers.akka-data-replication = "{{typeof(CustomDurableSerializer).AssemblyQualifiedName}}"
                    """) : customBinding ? ConfigurationFactory.ParseString($$"""
                    akka.actor.serializers.custom-durable = "{{typeof(CustomDurableSerializer).AssemblyQualifiedName}}"
                    akka.actor.serialization-bindings {
                        "Akka.DistributedData.Durable.DurableDataEnvelope, Akka.DistributedData" = custom-durable
                    }
                    """) : Config.Empty;
                var config = ConfigurationFactory.ParseString($$"""
                    akka.actor.serialization-v2 = {{(enabled ? "on" : "off")}}
                    akka.actor.provider = remote
                    akka.remote.dot-netty.tcp.port = 0
                    akka.cluster.distributed-data.durable.lmdb {
                        dir = "{{directory.Replace('\\', '/')}}"
                        map-size = 100 MiB
                        write-behind-interval = {{writeBehind}}
                    }
                    """).WithFallback(custom).WithFallback(DistributedData.DefaultConfig());
                var kit = nestedCrdt
                    ? new ActorTestKit(CanonicalCrdtSetup.Create(enabled, binding[5..], config), name, _output)
                    : new ActorTestKit(config, name, _output);
                try
                {
                    var store = kit.Sys.ActorOf(LmdbDurableStore.Props(
                        config.GetConfig("akka.cluster.distributed-data.durable")), "store");
                    var probe = kit.CreateTestProbe();
                    store.Tell(LoadAll.Instance, probe.Ref);
                    var first = await probe.ExpectMsgAsync<object>(Timeout);
                    var data = ImmutableDictionary<string, DurableDataEnvelope>.Empty;
                    if (first is LoadData loaded)
                    {
                        data = loaded.Data;
                        await probe.ExpectMsgAsync<LoadAllCompleted>(Timeout);
                    }
                    else
                        Assert.IsType<LoadAllCompleted>(first);
                    await action(kit, store, data);
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
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task Store(ActorTestKit kit, IActorRef store, string key, DurableDataEnvelope value)
    {
        var probe = kit.CreateTestProbe();
        store.Tell(new Store(key, value, new StoreReply("stored", "failed", probe.Ref)));
        Assert.Equal("stored", await probe.ExpectMsgAsync<string>(Timeout));
    }

    private static IReadOnlyDictionary<string, byte[]> ReadRecords(string directory)
    {
        using var environment = new LightningEnvironment(directory) { MapSize = 100 * 1024 * 1024, MaxDatabases = 1 };
        environment.Open(EnvironmentOpenFlags.NoLock);
        using var transaction = environment.BeginTransaction(TransactionBeginFlags.ReadOnly);
        using var database = transaction.OpenDatabase(LmdbDurableStore.DatabaseName);
        using var cursor = transaction.CreateCursor(database);
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (key, value) in cursor.AsEnumerable())
            result.Add(Encoding.UTF8.GetString(key.CopyToNewArray()), value.CopyToNewArray());
        return result;
    }

    /// <summary>A pre-existing application format, deliberately incompatible with the built-in id 12 bytes.</summary>
    public sealed class CustomDurableSerializer : SerializerWithStringManifest
    {
        private static readonly byte[] Magic = "custom-lmdb-777:"u8.ToArray();
        private readonly ReplicatorMessageSerializer _legacy;

        public CustomDurableSerializer(ExtendedActorSystem system) : base(system)
        {
            _legacy = new ReplicatorMessageSerializer(system);
        }

        public override int Identifier => 777;
        public override string Manifest(object obj) => "custom";

        public override byte[] ToBinary(object obj)
        {
            var payload = _legacy.ToBinary(obj);
            var bytes = new byte[Magic.Length + payload.Length];
            Magic.CopyTo(bytes, 0);
            payload.CopyTo(bytes, Magic.Length);
            return bytes;
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            if (manifest != "custom" || !bytes.AsSpan().StartsWith(Magic))
                throw new SerializationException("The custom LMDB id 777 wire prefix or manifest was lost.");
            return _legacy.FromBinary(bytes.AsSpan(Magic.Length).ToArray(), "P");
        }
    }
}
