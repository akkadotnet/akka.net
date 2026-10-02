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
            var config = SqliteSpecConfig.Create(db, SqliteTestMode.TT);
            var setup = BootstrapSetup.Create().WithConfig(config)
                .And(PersistencePluginSetup.Empty.WithEmbeddedPersistence())
                .And(PersistenceQuerySetup.Empty.WithEmbeddedReadJournal())
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
