//-----------------------------------------------------------------------
// <copyright file="SetupRegistrationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Persistence.Embedded.Internal;
using Akka.Persistence.Embedded.Query;
using Akka.Persistence.Query;
using Akka.Serialization;
using Akka.Streams;
using Akka.Streams.Dsl;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Embedded.Tests.Registration
{
    /// <summary>
    /// AppContext switches are process-wide, so a spec that flips <c>Akka.DynamicTypeLoading</c> never runs beside another.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class DynamicTypeLoadingCollection
    {
        public const string Name = "Akka.DynamicTypeLoading";
    }

    [Collection(DynamicTypeLoadingCollection.Name)]
    public class SetupRegistrationSpec
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        [Fact(DisplayName = "Should_start_plugins_through_setup_registration_When_dynamic_type_loading_is_off")]
        public async Task Should_start_plugins_through_setup_registration_When_dynamic_type_loading_is_off()
        {
            using var db = new SqliteTestDb();
            // the whole HOCON a user writes: which plugins are the default and where the database is. No class, no reference config.
            var config = ConfigurationFactory.ParseString($$"""
                akka.persistence.journal.plugin = "akka.persistence.journal.embedded"
                akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.embedded"
                akka.persistence.journal.embedded.connection-string = "{{db.HoconConnectionString}}"
                akka.persistence.snapshot-store.embedded.connection-string = "{{db.HoconConnectionString}}"
                akka.persistence.query.journal.embedded.refresh-interval = 100ms
                """);
            var setup = BootstrapSetup.Create().WithConfig(config)
                .And(PersistenceSetup.Create().WithEmbeddedPersistence())
                .And(SerializationSetup.Create(static system => ImmutableHashSet.Create(
                    SerializerDetails.Create("test-event", new TestEventSerializer(system), ImmutableHashSet.Create(typeof(TestEvent))))));

            var hadSwitch = AppContext.TryGetSwitch(SwitchName, out var previous);
            AppContext.SetSwitch(SwitchName, false);
            try
            {
                var system = (ExtendedActorSystem)ActorSystem.Create("setup-registration", setup);
                try
                {
                    var persistence = Persistence.Instance.Apply(system);
                    await persistence.JournalFor(null).Ask<Initialized>(EnsureInitialized.Instance, Timeout);
                    await persistence.SnapshotStoreFor(null).Ask<Initialized>(EnsureInitialized.Instance, Timeout);

                    var actor = system.ActorOf(Props.Create(() => new RecordingActor("registered")));
                    (await actor.Ask<int>("first", Timeout)).Should().Be(1);
                    (await actor.Ask<int>("second", Timeout)).Should().Be(2);

                    var readJournal = system.ReadJournalFor<SqliteReadJournal>(SqliteReadJournal.Identifier);
                    var events = await readJournal.CurrentEventsByPersistenceId("registered", 0, long.MaxValue)
                        .RunWith(Sink.Seq<EventEnvelope>(), system.Materializer()).WaitAsync(Timeout);
                    events.Select(e => ((TestEvent)e.Event).Value).Should().Equal("first", "second");
                }
                finally
                {
                    await system.Terminate().WaitAsync(Timeout);
                }
            }
            finally
            {
                AppContext.SetSwitch(SwitchName, !hadSwitch || previous);
            }
        }

        [Fact(DisplayName = "Should_start_two_registered_read_journals_with_gap_tracking_When_their_ids_differ")]
        public async Task Should_start_two_registered_read_journals_with_gap_tracking_When_their_ids_differ()
        {
            using var db = new SqliteTestDb();
            var config = ConfigurationFactory.ParseString($$"""
                akka.persistence.journal.plugin = "akka.persistence.journal.embedded"
                akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.embedded"
                akka.persistence.journal.embedded.connection-string = "{{db.HoconConnectionString}}"
                akka.persistence.snapshot-store.embedded.connection-string = "{{db.HoconConnectionString}}"
                akka.persistence.query.journal.embedded.journal-sequence-retrieval.enabled = on
                akka.persistence.query.journal.second.journal-sequence-retrieval.enabled = on
                akka.persistence.query.journal.second.write-plugin = "akka.persistence.journal.embedded"
                """);
            var setup = BootstrapSetup.Create().WithConfig(config)
                .And(PersistenceSetup.Create().WithEmbeddedPersistence().WithEmbeddedReadJournal("akka.persistence.query.journal.second"))
                .And(SerializationSetup.Create(static system => ImmutableHashSet.Create(
                    SerializerDetails.Create("test-event", new TestEventSerializer(system), ImmutableHashSet.Create(typeof(TestEvent))))));

            var system = (ExtendedActorSystem)ActorSystem.Create("two-registered", setup);
            try
            {
                var actor = system.ActorOf(Props.Create(() => new RecordingActor("two-registered")));
                (await actor.Ask<int>("only", Timeout)).Should().Be(1);

                // the second one used to fail with InvalidActorNameException: both trackers had the same name
                var first = system.ReadJournalFor<SqliteReadJournal>(SqliteReadJournal.Identifier);
                var second = system.ReadJournalFor<SqliteReadJournal>("akka.persistence.query.journal.second");
                foreach (var journal in new[] { first, second })
                {
                    var events = await journal.CurrentEventsByPersistenceId("two-registered", 0, long.MaxValue)
                        .RunWith(Sink.Seq<EventEnvelope>(), system.Materializer()).WaitAsync(Timeout);
                    events.Should().HaveCount(1);
                }
            }
            finally
            {
                await system.Terminate().WaitAsync(Timeout);
            }
        }

        private sealed class RecordingActor : UntypedPersistentActor
        {
            private int _count;

            public RecordingActor(string persistenceId)
            {
                PersistenceId = persistenceId;
            }

            public override string PersistenceId { get; }

            protected override void OnRecover(object message)
            {
                if (message is TestEvent)
                    _count++;
            }

            protected override void OnCommand(object message)
            {
                if (message is not string value)
                    return;

                var sender = Sender;
                Persist(new TestEvent(value), _ =>
                {
                    _count++;
                    sender.Tell(_count);
                });
            }
        }
    }
}
