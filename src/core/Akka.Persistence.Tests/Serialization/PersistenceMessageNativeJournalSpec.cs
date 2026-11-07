//-----------------------------------------------------------------------
// <copyright file="PersistenceMessageNativeJournalSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.Persistence.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Tests.Serialization;

/// <summary>Recovery crosses real unstamped protobuf files, not retained payload or envelope objects.</summary>
[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class PersistenceMessageNativeJournalSpec : AkkaSpec
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public PersistenceMessageNativeJournalSpec(ITestOutputHelper output) : base(output)
    {
    }

    [Fact(DisplayName = "Should_RecoverBothOuterWriters_When_TheJournalIsReopenedWithTheOtherImplementation")]
    public async Task Should_RecoverBothOuterWriters_When_TheJournalIsReopenedWithTheOtherImplementation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"persistence-message-native-{Guid.NewGuid():N}");
        var config = ConfigurationFactory.ParseString($$"""
            akka.persistence.journal.plugin = "envelope-file-journal"
            envelope-file-journal {
              class = "{{typeof(EnvelopeFileJournal).AssemblyQualifiedName}}"
              plugin-dispatcher = "akka.actor.default-dispatcher"
            }
            persistence-message-file-journal.dir = "{{directory.Replace("\\", "/")}}"
            akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.local"
            akka.persistence.snapshot-store.local.dir = "{{directory.Replace("\\", "/")}}/snapshots"
            """).WithFallback(Persistence.DefaultConfig());
        try
        {
            (string Name, bool Legacy, string[] Expected, string? Append)[] stages =
            [
                ("legacy-write", true, Array.Empty<string>(), "legacy-event"),
                ("native-read-write", false, new[] { "legacy-event" }, "native-event"),
                ("legacy-read", true, new[] { "legacy-event", "native-event" }, null)
            ];
            foreach (var stage in stages)
            {
                var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config));
                if (stage.Legacy)
                    setup = setup.WithSetup(SerializationSetup.Create(system => ImmutableHashSet.Create(SerializerDetails.Create(
                        "akka-persistence-message", new PersistenceMessageSerializer(system), ImmutableHashSet.Create(typeof(IMessage))))));
                var system = ActorSystem.Create($"message-journal-{stage.Name}", setup);
                InitializeLogger(system);
                try
                {
                    var writer = system.Serialization.FindSerializerForType(typeof(AtomicWrite));
                    writer.Identifier.Should().Be(7);
                    if (stage.Legacy)
                        writer.Should().BeOfType<PersistenceMessageSerializer>();
                    else
                        writer.Should().BeOfType<NativePersistenceMessageSerializer>();
                    var probe = CreateTestProbe(system);
                    var actor = system.ActorOf(Props.Create(() => new EventActor(probe.Ref)), "persistent");
                    (await probe.ExpectMsgAsync<Recovered>(Timeout)).Events.Should().Equal(stage.Expected);
                    if (stage.Append is not null)
                    {
                        actor.Tell(new Append(stage.Append));
                        (await probe.ExpectMsgAsync<Persisted>(Timeout)).Events.Should().Equal(stage.Expected.Append(stage.Append));
                    }
                }
                finally
                {
                    await system.Terminate().WaitAsync(Timeout);
                }
            }
            Directory.EnumerateFiles(directory, "*.events", SearchOption.AllDirectories).Should().HaveCount(2);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record Append(string Value);
    private sealed record Recovered(string[] Events);
    private sealed record Persisted(string[] Events);

    private sealed class EventActor : ReceivePersistentActor
    {
        private readonly List<string> _events = new();

        public EventActor(IActorRef probe)
        {
            Recover<string>(value => _events.Add(value));
            Recover<RecoveryCompleted>(_ => probe.Tell(new Recovered(_events.ToArray())));
            Command<Append>(message => Persist(message.Value, value =>
            {
                _events.Add(value);
                probe.Tell(new Persisted(_events.ToArray()));
            }));
        }

        public override string PersistenceId => "native-message-envelope";
    }

    /// <summary>
    /// Adapted from #8407's WireFormatJournal / Delivery's FileJournal. This variant stores each
    /// whole AtomicWrite protobuf without an outer serializer id or manifest, matching type-bound plugins.
    /// </summary>
    public sealed class EnvelopeFileJournal : AsyncWriteJournal
    {
        private readonly string _directory = Context.System.Settings.Config.GetString("persistence-message-file-journal.dir");
        private readonly Akka.Serialization.Serialization _serialization = Context.System.Serialization;

        protected override Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages, CancellationToken cancellationToken)
        {
            foreach (var write in messages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(EventDirectory(write.PersistenceId), write.LowestSequenceNr.ToString("D20", CultureInfo.InvariantCulture) + ".events");
                var bytes = _serialization.FindSerializerFor(write).ToBinary(write);
                File.WriteAllBytes(path + ".tmp", bytes);
                File.Move(path + ".tmp", path, overwrite: true);
            }
            // AsyncWriteJournal's contract uses null to mean every atomic write succeeded.
            return Task.FromResult<IImmutableList<Exception>>(null!);
        }

        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ReadRecords(persistenceId).Select(record => record.SequenceNr).DefaultIfEmpty().Max());
        }

        public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr,
            long toSequenceNr, long max, Action<IPersistentRepresentation> recoveryCallback)
        {
            var deletedTo = ReadDeletedTo(persistenceId);
            foreach (var record in ReadRecords(persistenceId).Where(record => record.SequenceNr > deletedTo &&
                         record.SequenceNr >= fromSequenceNr && record.SequenceNr <= toSequenceNr).Take((int)Math.Min(max, int.MaxValue)))
                recoveryCallback(record);
            return Task.CompletedTask;
        }

        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(EventDirectory(persistenceId), "deleted-to");
            using (var writer = new BinaryWriter(File.Create(path + ".tmp")))
                writer.Write(Math.Max(ReadDeletedTo(persistenceId), toSequenceNr));
            File.Move(path + ".tmp", path, overwrite: true);
            return Task.CompletedTask;
        }

        private long ReadDeletedTo(string persistenceId)
        {
            var path = Path.Combine(EventDirectory(persistenceId), "deleted-to");
            if (!File.Exists(path))
                return 0;
            using var reader = new BinaryReader(File.OpenRead(path));
            return reader.ReadInt64();
        }

        private IEnumerable<IPersistentRepresentation> ReadRecords(string persistenceId)
        {
            var serializer = _serialization.FindSerializerForType(typeof(AtomicWrite));
            foreach (var path in Directory.GetFiles(EventDirectory(persistenceId), "*.events").OrderBy(path => path, StringComparer.Ordinal))
            {
                var write = serializer.FromBinary<AtomicWrite>(File.ReadAllBytes(path));
                foreach (var record in (IEnumerable<IPersistentRepresentation>)write.Payload)
                    yield return record;
            }
        }

        private string EventDirectory(string persistenceId)
        {
            var path = Path.Combine(_directory, Uri.EscapeDataString(persistenceId));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
