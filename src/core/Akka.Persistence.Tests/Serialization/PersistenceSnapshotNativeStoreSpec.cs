//-----------------------------------------------------------------------
// <copyright file="PersistenceSnapshotNativeStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Persistence.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using SnapshotEnvelope = Akka.Persistence.Serialization.Snapshot;

namespace Akka.Persistence.Tests.Serialization;

/// <summary>Real LocalSnapshotStore recovery across native/legacy implementations of the same unstamped envelope.</summary>
[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class PersistenceSnapshotNativeStoreSpec : AkkaSpec
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public PersistenceSnapshotNativeStoreSpec(ITestOutputHelper output) : base(output)
    {
    }

    [Fact(DisplayName = "Should_RecoverBothWriters_When_TheLocalStoreIsReopenedWithTheOtherImplementation")]
    public async Task Should_RecoverBothWriters_When_TheLocalStoreIsReopenedWithTheOtherImplementation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"snapshot-native-{Guid.NewGuid():N}");
        var config = ConfigurationFactory.ParseString($$"""
            akka.persistence.journal.plugin = "akka.persistence.journal.inmem"
            akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.local"
            akka.persistence.snapshot-store.local.dir = "{{directory.Replace("\\", "/")}}"
            """).WithFallback(Persistence.DefaultConfig());
        try
        {
            (string Name, bool Legacy, string? Expected, string? Save)[] stages =
            [
                ("legacy-write", true, null, "legacy-snapshot"),
                ("native-read-write", false, "legacy-snapshot", "native-snapshot"),
                ("legacy-read", true, "native-snapshot", null)
            ];
            foreach (var stage in stages)
            {
                var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config));
                if (stage.Legacy)
                    setup = setup.WithSetup(SerializationSetup.Create(system => ImmutableHashSet.Create(SerializerDetails.Create(
                        "akka-persistence-snapshot", new PersistenceSnapshotSerializer(system), ImmutableHashSet.Create(typeof(SnapshotEnvelope))))));
                var system = ActorSystem.Create($"snapshot-store-{stage.Name}", setup);
                InitializeLogger(system);
                try
                {
                    var serializer = system.Serialization.FindSerializerForType(typeof(SnapshotEnvelope));
                    serializer.Identifier.Should().Be(8);
                    if (stage.Legacy)
                        serializer.Should().BeOfType<PersistenceSnapshotSerializer>();
                    else
                        serializer.Should().BeOfType<NativePersistenceSnapshotSerializer>();
                    var probe = CreateTestProbe(system);
                    var actor = system.ActorOf(Props.Create(() => new SnapshotActor(probe.Ref)), "persistent");
                    (await probe.ExpectMsgAsync<Recovered>(Timeout)).Value.Should().Be(stage.Expected);
                    if (stage.Save is not null)
                    {
                        actor.Tell(new Save(stage.Save));
                        await probe.ExpectMsgAsync<SaveSnapshotSuccess>(Timeout);
                    }
                }
                finally
                {
                    await system.Terminate().WaitAsync(Timeout);
                }
            }
            Directory.EnumerateFiles(directory).Should().NotBeEmpty("the proof must pass through files, not an in-memory shortcut");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record Save(string Value);
    private sealed record Recovered(string? Value);

    private sealed class SnapshotActor : ReceivePersistentActor
    {
        private string? _value;

        public SnapshotActor(IActorRef probe)
        {
            Recover<SnapshotOffer>(offer => _value = (string)offer.Snapshot);
            Recover<RecoveryCompleted>(_ => probe.Tell(new Recovered(_value)));
            Command<Save>(message => SaveSnapshot(message.Value));
            Command<SaveSnapshotSuccess>(message => probe.Tell(message));
            Command<SaveSnapshotFailure>(message => probe.Tell(message));
        }

        public override string PersistenceId => "native-snapshot-compatibility";
    }
}
