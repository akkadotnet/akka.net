// -----------------------------------------------------------------------
//  <copyright file="PersistenceSetupHostingSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Persistence.Journal;
using Akka.Persistence.Query;
using Akka.Persistence.Query.InMemory;
using Akka.Persistence.Snapshot;
using Akka.Streams;
using Akka.Streams.Dsl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Akka.Persistence.Hosting.Tests;

/// <summary>
/// AppContext switches are process-wide, so a spec that flips <c>Akka.DynamicTypeLoading</c> never runs beside another.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DynamicTypeLoadingCollection
{
    public const string Name = "Akka.DynamicTypeLoading";
}

/// <summary>
/// An ActorSystem built purely through the Akka.Persistence.Hosting builders has to start, persist and recover
/// with <c>Akka.DynamicTypeLoading</c> off, and nothing may change for the apps that leave it on.
/// </summary>
[Collection(DynamicTypeLoadingCollection.Name)]
public class PersistenceSetupHostingSpecs
{
    private const string SwitchName = "Akka.DynamicTypeLoading";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // ---- the in-memory builders ----

    [Theory(DisplayName = "WithInMemoryJournal should run an event adapter When the system starts through Hosting")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_run_an_event_adapter_When_the_system_starts_through_Hosting(bool dynamicTypeLoading)
    {
        WrapAdapter.Reset();

        await RunAsync(dynamicTypeLoading,
            builder => builder
                .WithInMemoryJournal(journal => journal.AddEventAdapter<WrapAdapter>("wrap", new[] { typeof(string) }))
                .WithInMemorySnapshotStore(),
            async system =>
            {
                await PersistAndRecoverAsync(system, "p-inmem", null, null);

                WrapAdapter.ToJournalCalls.Should().BeGreaterThan(0, "the adapter wrote the events");
                WrapAdapter.FromJournalCalls.Should().BeGreaterThan(0, "the adapter read them back on recovery");
            });
    }

    [Theory(DisplayName = "WithInMemoryJournal should create an adapter through the ActorSystem constructor When the adapter has one")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_create_an_adapter_through_the_ActorSystem_constructor_When_the_adapter_has_one(bool dynamicTypeLoading)
    {
        SystemAdapter.Reset();

        await RunAsync(dynamicTypeLoading,
            builder => builder
                .WithInMemoryJournal(journal => journal.AddWriteEventAdapter<SystemAdapter>("system-adapter", new[] { typeof(string) }))
                .WithInMemorySnapshotStore(),
            async system =>
            {
                await PersistAndRecoverAsync(system, "p-ctor", null, null);

                SystemAdapter.Created.Should().Be(1);
                SystemAdapter.ToJournalCalls.Should().BeGreaterThan(0);
            });
    }

    [Theory(DisplayName = "AddReadEventAdapter should run a read-only adapter When the system starts through Hosting")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_run_a_read_only_adapter_When_the_system_starts_through_Hosting(bool dynamicTypeLoading)
    {
        UnwrapAdapter.Reset();

        await RunAsync(dynamicTypeLoading,
            builder => builder
                .WithInMemoryJournal(journal => journal
                    .AddWriteEventAdapter<WrapOnlyAdapter>("wrap", new[] { typeof(string) })
                    .AddReadEventAdapter<UnwrapAdapter>("unwrap", new[] { typeof(string) }))
                .WithInMemorySnapshotStore(),
            async system =>
            {
                await PersistAndRecoverAsync(system, "p-read", null, null);

                UnwrapAdapter.FromJournalCalls.Should().BeGreaterThan(0, "the read adapter ran on recovery");
            });
    }

    [Theory(DisplayName = "AddWriteEventAdapter should create an adapter that takes the ActorSystem When the switch is off or on")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_create_an_adapter_that_takes_the_ActorSystem_When_the_switch_is_off_or_on(bool dynamicTypeLoading)
    {
        PlainSystemAdapter.Reset();

        // core's reflection path calls Activator.CreateInstance(type, system), which also takes an ActorSystem parameter
        await RunAsync(dynamicTypeLoading,
            builder => builder
                .WithInMemoryJournal(journal => journal.AddWriteEventAdapter<PlainSystemAdapter>("plain-system", new[] { typeof(string) }))
                .WithInMemorySnapshotStore(),
            async system =>
            {
                await PersistAndRecoverAsync(system, "p-plain", null, null);

                PlainSystemAdapter.Created.Should().Be(1);
            });
    }

    [Fact(DisplayName = "AddWriteEventAdapter should not build an adapter When it is bound to no type and the switch is on")]
    public async Task Should_not_build_an_adapter_When_it_is_bound_to_no_type_and_the_switch_is_on()
    {
        SystemAdapter.Reset();

        // no bound type means no HOCON, so the JIT never built this adapter, and it still does not
        await RunAsync(true,
            builder => builder
                .WithInMemoryJournal(journal => journal.AddWriteEventAdapter<SystemAdapter>("unbound", Array.Empty<Type>()))
                .WithInMemorySnapshotStore(),
            async system =>
            {
                await PersistAndRecoverAsync(system, "p-unbound", null, null);

                SystemAdapter.Created.Should().Be(0);
            });
    }

    [Fact(DisplayName = "AddWriteEventAdapter should throw a NullReferenceException When the bound types are null")]
    public void Should_throw_a_NullReferenceException_When_the_bound_types_are_null()
    {
        var builder = new AkkaPersistenceJournalBuilder("null-types", new AkkaConfigurationBuilder(new ServiceCollection(), "null-types"));

        // as it always did: the sequence is enumerated without a check
        Assert.Throws<NullReferenceException>(() => builder.AddWriteEventAdapter<SystemAdapter>("adapter", (IEnumerable<Type>)null!));
    }

    // ---- plugins whose options name their types in code ----

    [Theory(DisplayName = "WithJournalAndSnapshot should start plugins whose options name their types When there is no HOCON class")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_start_plugins_whose_options_name_their_types_When_there_is_no_HOCON_class(bool dynamicTypeLoading)
    {
        WrapAdapter.Reset();
        var journal = new TestJournalOptions<JournalA>("custom") { IsDefaultPlugin = true };
        var snapshot = new TestSnapshotOptions<SnapshotA>("custom") { IsDefaultPlugin = true };

        await RunAsync(dynamicTypeLoading,
            builder => builder.WithJournalAndSnapshot(journal, snapshot,
                configureJournal: j => j.AddEventAdapter<WrapAdapter>("wrap", new[] { typeof(string) }),
                configureSnapshot: null),
            async system =>
            {
                var persistence = Persistence.Instance.Apply(system);
                PropsOf(persistence.JournalFor(null)).Type.Should().Be(typeof(JournalA));
                PropsOf(persistence.SnapshotStoreFor(null)).Type.Should().Be(typeof(SnapshotA));

                await PersistAndRecoverAsync(system, "p-custom", null, null);
                WrapAdapter.ToJournalCalls.Should().BeGreaterThan(0);
            });
    }

    [Fact(DisplayName = "WithJournal should load the HOCON class When the options name another type and the switch is on")]
    public async Task Should_load_the_HOCON_class_When_the_options_name_another_type_and_the_switch_is_on()
    {
        // HOCON names JournalB and the options name JournalA: with reflection on HOCON `class` wins, as it always did
        var journal = new TestJournalOptions<JournalA>("both", className: typeof(JournalB).AssemblyQualifiedName!) { IsDefaultPlugin = true };
        var snapshot = new TestSnapshotOptions<SnapshotA>("both", className: typeof(SnapshotB).AssemblyQualifiedName!) { IsDefaultPlugin = true };

        await RunAsync(true,
            builder => builder.WithJournalAndSnapshot(journal, snapshot),
            async system =>
            {
                var persistence = Persistence.Instance.Apply(system);

                PropsOf(persistence.JournalFor(null)).Type.Should().Be(typeof(JournalB));
                PropsOf(persistence.SnapshotStoreFor(null)).Type.Should().Be(typeof(SnapshotB));
                await PersistAndRecoverAsync(system, "p-both", null, null);
            });
    }

    [Fact(DisplayName = "WithJournal should throw naming both types When the options name another type than the HOCON class and the switch is off")]
    public async Task Should_throw_naming_both_types_When_the_options_name_another_type_than_the_HOCON_class_and_the_switch_is_off()
    {
        var journal = new TestJournalOptions<JournalA>("clash", className: typeof(JournalB).AssemblyQualifiedName!) { IsDefaultPlugin = true };
        var snapshot = new TestSnapshotOptions<SnapshotA>("clash", className: typeof(SnapshotB).AssemblyQualifiedName!) { IsDefaultPlugin = true };

        await RunAsync(false,
            builder => builder.WithJournalAndSnapshot(journal, snapshot),
            system =>
            {
                var persistence = Persistence.Instance.Apply(system);

                var journalError = Assert.Throws<ConfigurationException>(() => persistence.JournalFor(null));
                journalError.Message.Should().Contain(typeof(JournalA).FullName!).And.Contain(typeof(JournalB).FullName!);

                var snapshotError = Assert.Throws<ConfigurationException>(() => persistence.SnapshotStoreFor(null));
                snapshotError.Message.Should().Contain(typeof(SnapshotA).FullName!).And.Contain(typeof(SnapshotB).FullName!);
                return Task.CompletedTask;
            });
    }

    [Theory(DisplayName = "WithJournal should start the plugins When the HOCON class names the type the options name")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_start_the_plugins_When_the_HOCON_class_names_the_type_the_options_name(bool dynamicTypeLoading)
    {
        var journal = new TestJournalOptions<JournalA>("same", className: typeof(JournalA).AssemblyQualifiedName!) { IsDefaultPlugin = true };
        var snapshot = new TestSnapshotOptions<SnapshotA>("same", className: typeof(SnapshotA).AssemblyQualifiedName!) { IsDefaultPlugin = true };

        await RunAsync(dynamicTypeLoading,
            builder => builder.WithJournalAndSnapshot(journal, snapshot),
            async system =>
            {
                var persistence = Persistence.Instance.Apply(system);

                PropsOf(persistence.JournalFor(null)).Type.Should().Be(typeof(JournalA));
                PropsOf(persistence.SnapshotStoreFor(null)).Type.Should().Be(typeof(SnapshotA));
                await PersistAndRecoverAsync(system, "p-same", null, null);
            });
    }

    [Fact(DisplayName = "AddEventAdapter should throw naming the binding When a hand-written binding of another type points at a Hosting adapter and the switch is off")]
    public async Task Should_throw_naming_the_binding_When_a_hand_written_binding_of_another_type_points_at_a_Hosting_adapter_and_the_switch_is_off()
    {
        var extraBinding = $$"""
            akka.persistence.journal.inmem.event-adapter-bindings."{{typeof(EventTwo).FullName}}, {{typeof(EventTwo).Assembly.GetName().Name}}" = [x]
            """;

        await RunAsync(false,
            builder => builder
                .WithInMemoryJournal(journal => journal.AddEventAdapter<FirstNamedAdapter>("x", new[] { typeof(EventOne) }))
                .AddHocon(extraBinding, HoconAddMode.Append),
            system =>
            {
                var exception = Assert.Throws<ConfigurationException>(() => Persistence.Instance.Apply(system).AdaptersFor("akka.persistence.journal.inmem"));

                exception.Message.Should().Contain("akka.persistence.journal.inmem.event-adapter-bindings");
                exception.Message.Should().Contain(typeof(EventTwo).FullName!);
                return Task.CompletedTask;
            });
    }

    [Theory(DisplayName = "WithJournal should start every plugin When several calls register different plugin ids")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_start_every_plugin_When_several_calls_register_different_plugin_ids(bool dynamicTypeLoading)
    {
        var journalOne = new TestJournalOptions<JournalA>("one") { IsDefaultPlugin = true };
        var journalTwo = new TestJournalOptions<JournalB>("two");
        var snapshotOne = new TestSnapshotOptions<SnapshotA>("one") { IsDefaultPlugin = true };
        var snapshotTwo = new TestSnapshotOptions<SnapshotB>("two");

        await RunAsync(dynamicTypeLoading,
            builder => builder
                .WithJournal(journalOne)
                .WithSnapshot(snapshotOne)
                .WithJournal(journalTwo)
                .WithSnapshot(snapshotTwo),
            async system =>
            {
                var persistence = Persistence.Instance.Apply(system);

                // the default plugins are the ones that asked to be, the others are there by id
                PropsOf(persistence.JournalFor(null)).Type.Should().Be(typeof(JournalA));
                PropsOf(persistence.SnapshotStoreFor(null)).Type.Should().Be(typeof(SnapshotA));
                PropsOf(persistence.JournalFor(journalTwo.PluginId)).Type.Should().Be(typeof(JournalB));
                PropsOf(persistence.SnapshotStoreFor(snapshotTwo.PluginId)).Type.Should().Be(typeof(SnapshotB));

                await PersistAndRecoverAsync(system, "p-default", null, null);
                await PersistAndRecoverAsync(system, "p-second", journalTwo.PluginId, snapshotTwo.PluginId);
            });
    }

    [Theory(DisplayName = "WithJournal should let the later call win When one plugin id is configured twice")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_let_the_later_call_win_When_one_plugin_id_is_configured_twice(bool dynamicTypeLoading)
    {
        var first = new TestJournalOptions<JournalA>("same") { IsDefaultPlugin = true };
        var second = new TestJournalOptions<JournalB>("same") { IsDefaultPlugin = true };

        await RunAsync(dynamicTypeLoading,
            builder => builder.WithJournal(first).WithInMemorySnapshotStore().WithJournal(second),
            async system =>
            {
                PropsOf(Persistence.Instance.Apply(system).JournalFor(first.PluginId)).Type.Should().Be(typeof(JournalB));
                await PersistAndRecoverAsync(system, "p-twice", null, null);
            });
    }

    [Theory(DisplayName = "WithJournal should merge an adapter added in a later call When the journal was configured earlier")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_merge_an_adapter_added_in_a_later_call_When_the_journal_was_configured_earlier(bool dynamicTypeLoading)
    {
        WrapAdapter.Reset();
        var journal = new TestJournalOptions<JournalA>("late") { IsDefaultPlugin = true };

        await RunAsync(dynamicTypeLoading,
            builder => builder
                .WithJournal(journal)
                .WithInMemorySnapshotStore()
                .WithJournal(journal, j => j.AddEventAdapter<WrapAdapter>("wrap", new[] { typeof(string) })),
            async system =>
            {
                PropsOf(Persistence.Instance.Apply(system).JournalFor(null)).Type.Should().Be(typeof(JournalA), "the later call did not replace the journal");

                await PersistAndRecoverAsync(system, "p-late", null, null);
                WrapAdapter.ToJournalCalls.Should().BeGreaterThan(0);
            });
    }

    [Theory(DisplayName = "WithJournal should merge an adapter added in an earlier call When the journal is configured later")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_merge_an_adapter_added_in_an_earlier_call_When_the_journal_is_configured_later(bool dynamicTypeLoading)
    {
        WrapAdapter.Reset();
        var journal = new TestJournalOptions<JournalA>("early") { IsDefaultPlugin = true };

        await RunAsync(dynamicTypeLoading,
            builder => builder
                .WithJournal(journal, j => j.AddEventAdapter<WrapAdapter>("wrap", new[] { typeof(string) }))
                .WithInMemorySnapshotStore()
                .WithJournal(journal),
            async system =>
            {
                PropsOf(Persistence.Instance.Apply(system).JournalFor(null)).Type.Should().Be(typeof(JournalA));

                await PersistAndRecoverAsync(system, "p-early", null, null);
                WrapAdapter.ToJournalCalls.Should().BeGreaterThan(0);
            });
    }

    [Theory(DisplayName = "AddEventAdapter should bind the event types of both calls When an adapter name is added twice")]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Should_bind_the_event_types_of_both_calls_When_an_adapter_name_is_added_twice(bool dynamicTypeLoading, bool inSeparateCalls)
    {
        // In HOCON the later adapter type wins the name and the bindings merge, so the name ends up bound to both event types.
        // With the switch off the setup has to give the same answer.
        await RunAsync(dynamicTypeLoading,
            builder =>
            {
                if (inSeparateCalls)
                {
                    builder
                        .WithInMemoryJournal(journal => journal.AddEventAdapter<FirstNamedAdapter>("x", new[] { typeof(EventOne) }))
                        .WithInMemoryJournal(journal => journal.AddEventAdapter<SecondNamedAdapter>("x", new[] { typeof(EventTwo) }));
                }
                else
                {
                    builder.WithInMemoryJournal(journal => journal
                        .AddEventAdapter<FirstNamedAdapter>("x", new[] { typeof(EventOne) })
                        .AddEventAdapter<SecondNamedAdapter>("x", new[] { typeof(EventTwo) }));
                }
            },
            system =>
            {
                var adapters = Persistence.Instance.Apply(system).AdaptersFor("akka.persistence.journal.inmem");

                adapters.Get<EventOne>().Should().BeOfType<SecondNamedAdapter>("the later type wins the name");
                adapters.Get<EventTwo>().Should().BeOfType<SecondNamedAdapter>();
                return Task.CompletedTask;
            });
    }

    // ---- read journals ----

    [Theory(DisplayName = "WithJournal should register the plugin's default read journal When the options name its provider")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_register_the_default_read_journal_When_the_options_name_its_provider(bool dynamicTypeLoading)
    {
        var journal = new QueryJournalOptions("queryable") { IsDefaultPlugin = true };

        await RunAsync(dynamicTypeLoading,
            builder => builder
                .WithJournal(journal)
                .WithInMemorySnapshotStore()
                // the plugin's own query defaults, which a plugin's Hosting package adds as HOCON today
                .AddHocon(InMemoryReadJournal.DefaultConfiguration(), HoconAddMode.Append),
            async system =>
            {
                await PersistAndRecoverAsync(system, "p-query", null, null);

                // the journal's read journal, at the path the options name; with the switch off the HOCON `class` plays no part
                var readJournal = PersistenceQuery.Get(system).ReadJournalFor<InMemoryReadJournal>(QueryJournalOptions.ReadJournalPath);
                var events = await readJournal
                    .CurrentEventsByPersistenceId("p-query", 0, long.MaxValue)
                    .RunWith(Sink.Seq<EventEnvelope>(), system.Materializer())
                    .WaitAsync(Timeout);

                events.Select(e => e.Event).Should().Equal("a", "b");
            });
    }

    // ---- the sharding migration adapter ----

    [Fact(DisplayName = "WithClusterShardingJournalMigrationAdapter should register nothing in code When Akka.Cluster.Sharding is absent")]
    public void Should_register_nothing_in_code_When_Akka_Cluster_Sharding_is_absent()
    {
        // this test project does not deploy Akka.Cluster.Sharding: the HOCON is written as always and the setup stays empty
        var builder = new AkkaConfigurationBuilder(new ServiceCollection(), "migration");
        builder.WithClusterShardingJournalMigrationAdapter("akka.persistence.journal.sharding");

        Type.GetType("Akka.Cluster.Sharding.ShardCoordinator, Akka.Cluster.Sharding").Should().BeNull();
        builder.Setups.OfType<PersistenceSetup>().Should().BeEmpty();
        builder.Configuration.Value.GetString("akka.persistence.journal.sharding.event-adapters.coordinator-migration")
            .Should().Be("Akka.Cluster.Sharding.OldCoordinatorStateMigrationEventAdapter, Akka.Cluster.Sharding");
    }

    // ---- nothing changes for apps that do not register ----

    [Fact(DisplayName = "WithJournal should start a plugin through its HOCON class When its options name no type and the switch is on")]
    public async Task Should_start_a_plugin_through_its_HOCON_class_When_its_options_name_no_type_and_the_switch_is_on()
    {
        var journal = new TestJournalOptions("legacy", className: typeof(JournalA).AssemblyQualifiedName!) { IsDefaultPlugin = true };

        await RunAsync(true,
            builder => builder.WithJournal(journal).WithInMemorySnapshotStore(),
            async system =>
            {
                PropsOf(Persistence.Instance.Apply(system).JournalFor(null)).Type.Should().Be(typeof(JournalA));
                await PersistAndRecoverAsync(system, "p-legacy", null, null);
            });
    }

    [Fact(DisplayName = "WithJournal should fail the plugin start and name the Hosting builders When its options name no type and the switch is off")]
    public async Task Should_fail_the_plugin_start_and_name_the_Hosting_builders_When_its_options_name_no_type_and_the_switch_is_off()
    {
        var journal = new TestJournalOptions("legacy", className: typeof(JournalA).AssemblyQualifiedName!) { IsDefaultPlugin = true };

        await RunAsync(false,
            builder => builder.WithJournal(journal).WithInMemorySnapshotStore(),
            system =>
            {
                var exception = Assert.Throws<ConfigurationException>(() => Persistence.Instance.Apply(system).JournalFor(null));

                exception.Message.Should().Contain(journal.PluginId + ".class");
                exception.Message.Should().Contain("Akka.DynamicTypeLoading");
                exception.Message.Should().Contain("Akka.Persistence.Hosting");
                return Task.CompletedTask;
            });
    }

    // ---- helpers ----

    private static Props PropsOf(IActorRef actor) => ((ActorRefWithCell)actor).Underlying.Props;

    /// <summary>Persists a and b, snapshots, stops the actor and recovers a new incarnation from the snapshot and journal.</summary>
    private static async Task PersistAndRecoverAsync(ActorSystem system, string persistenceId, string? journalId, string? snapshotId)
    {
        var first = system.ActorOf(Props.Create(() => new HostedActor(persistenceId, journalId, snapshotId)));
        (await first.Ask<string>("a", Timeout)).Should().Be("a");
        (await first.Ask<string>("snap", Timeout)).Should().Be("snapshot-saved");
        (await first.Ask<string>("b", Timeout)).Should().Be("b");
        await first.GracefulStop(Timeout);

        var second = system.ActorOf(Props.Create(() => new HostedActor(persistenceId, journalId, snapshotId)));
        (await second.Ask<string>("state", Timeout)).Should().Be("a,b");
        await second.GracefulStop(Timeout);
    }

    private static async Task RunAsync(bool dynamicTypeLoading, Action<AkkaConfigurationBuilder> configure, Func<ActorSystem, Task> body)
    {
        var hadSwitch = AppContext.TryGetSwitch(SwitchName, out var previous);
        AppContext.SetSwitch(SwitchName, dynamicTypeLoading);
        try
        {
            using var host = new HostBuilder()
                .ConfigureServices(services => services.AddAkka("hosting-spec-" + Guid.NewGuid().ToString("N").Substring(0, 8), builder => configure(builder)))
                .Build();

            await host.StartAsync();
            try
            {
                await body(host.Services.GetRequiredService<ActorSystem>());
            }
            finally
            {
                await host.StopAsync();
            }
        }
        finally
        {
            AppContext.SetSwitch(SwitchName, !hadSwitch || previous);
        }
    }

    // ---- plugin and event types ----

    public sealed class JournalA : MemoryJournal
    {
    }

    public sealed class JournalB : MemoryJournal
    {
    }

    public sealed class SnapshotA : MemorySnapshotStore
    {
    }

    public sealed class SnapshotB : MemorySnapshotStore
    {
    }

    private static Config PluginDefaults(string? className) => ConfigurationFactory.ParseString(
        $"plugin-dispatcher = \"akka.actor.default-dispatcher\"\n{(className is null ? "" : $"class = \"{className}\"")}");

    /// <summary>Options that name no type: the plugin starts from its HOCON <c>class</c> only.</summary>
    public sealed class TestJournalOptions : JournalOptions
    {
        private readonly string? _className;

        public TestJournalOptions(string identifier, string? className = null) : base(false)
        {
            Identifier = identifier;
            _className = className;
        }

        public override string Identifier { get; set; }

        protected override Config InternalDefaultConfig => PluginDefaults(_className);
    }

    public sealed class TestJournalOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)] TJournal>
        : JournalOptions<TJournal> where TJournal : ActorBase
    {
        private readonly string? _className;

        public TestJournalOptions(string identifier, string? className = null) : base(false)
        {
            Identifier = identifier;
            _className = className;
        }

        public override string Identifier { get; set; }

        protected override Config InternalDefaultConfig => PluginDefaults(_className);
    }

    public sealed class TestSnapshotOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)] TStore>
        : SnapshotOptions<TStore> where TStore : ActorBase
    {
        private readonly string? _className;

        public TestSnapshotOptions(string identifier, string? className = null) : base(false)
        {
            Identifier = identifier;
            _className = className;
        }

        public override string Identifier { get; set; }

        protected override Config InternalDefaultConfig => PluginDefaults(_className);
    }

    /// <summary>A journal that ships a default read journal at a path other than <c>akka.persistence.query.journal.{Identifier}</c>.</summary>
    public sealed class QueryJournalOptions : JournalOptions<JournalA, InMemoryReadJournalProvider>
    {
        // the in-memory read journal's own path, where its HOCON defaults sit
        public const string ReadJournalPath = "akka.persistence.query.journal.inmem";

        public QueryJournalOptions(string identifier) : base(false)
        {
            Identifier = identifier;
        }

        public override string Identifier { get; set; }

        protected override string ReadJournalPluginId => ReadJournalPath;

        protected override Config InternalDefaultConfig => PluginDefaults(null);
    }

    /// <summary>Turns a journaled string into "w:string" and back, and counts both.</summary>
    public sealed class WrapAdapter : IEventAdapter
    {
        private static int _toJournal;
        private static int _fromJournal;

        public static int ToJournalCalls => Volatile.Read(ref _toJournal);
        public static int FromJournalCalls => Volatile.Read(ref _fromJournal);

        public static void Reset()
        {
            _toJournal = 0;
            _fromJournal = 0;
        }

        public string Manifest(object evt) => "wrap";

        public object ToJournal(object evt)
        {
            Interlocked.Increment(ref _toJournal);
            return evt is string s ? "w:" + s : evt;
        }

        public IEventSequence FromJournal(object evt, string manifest)
        {
            Interlocked.Increment(ref _fromJournal);
            return EventSequence.Single(evt is string s && s.StartsWith("w:", StringComparison.Ordinal) ? s.Substring(2) : evt);
        }
    }

    public sealed class EventOne
    {
    }

    public sealed class EventTwo
    {
    }

    public sealed class FirstNamedAdapter : IEventAdapter
    {
        public string Manifest(object evt) => "first";

        public object ToJournal(object evt) => evt;

        public IEventSequence FromJournal(object evt, string manifest) => EventSequence.Single(evt);
    }

    public sealed class SecondNamedAdapter : IEventAdapter
    {
        public string Manifest(object evt) => "second";

        public object ToJournal(object evt) => evt;

        public IEventSequence FromJournal(object evt, string manifest) => EventSequence.Single(evt);
    }

    /// <summary>Wraps strings on the way in and leaves the way out alone.</summary>
    public sealed class WrapOnlyAdapter : IWriteEventAdapter
    {
        public string Manifest(object evt) => "wrap";

        public object ToJournal(object evt) => evt is string s ? "w:" + s : evt;
    }

    /// <summary>Unwraps what <see cref="WrapOnlyAdapter"/> wrapped and counts the reads.</summary>
    public sealed class UnwrapAdapter : IReadEventAdapter
    {
        private static int _fromJournal;

        public static int FromJournalCalls => Volatile.Read(ref _fromJournal);

        public static void Reset() => _fromJournal = 0;

        public IEventSequence FromJournal(object evt, string manifest)
        {
            Interlocked.Increment(ref _fromJournal);
            return EventSequence.Single(evt is string s && s.StartsWith("w:", StringComparison.Ordinal) ? s.Substring(2) : evt);
        }
    }

    /// <summary>A write adapter whose only constructor takes an <see cref="ActorSystem"/>, which core's reflection path accepts.</summary>
    public sealed class PlainSystemAdapter : IWriteEventAdapter
    {
        private static int _created;

        public static int Created => Volatile.Read(ref _created);

        public static void Reset() => _created = 0;

        public PlainSystemAdapter(ActorSystem system)
        {
            system.Should().NotBeNull();
            Interlocked.Increment(ref _created);
        }

        public string Manifest(object evt) => string.Empty;

        public object ToJournal(object evt) => evt;
    }

    /// <summary>A write adapter whose only constructor takes the <see cref="ExtendedActorSystem"/>.</summary>
    public sealed class SystemAdapter : IWriteEventAdapter
    {
        private static int _created;
        private static int _toJournal;

        public static int Created => Volatile.Read(ref _created);
        public static int ToJournalCalls => Volatile.Read(ref _toJournal);

        public static void Reset()
        {
            _created = 0;
            _toJournal = 0;
        }

        public SystemAdapter(ExtendedActorSystem system)
        {
            system.Should().NotBeNull();
            Interlocked.Increment(ref _created);
        }

        public string Manifest(object evt) => string.Empty;

        public object ToJournal(object evt)
        {
            Interlocked.Increment(ref _toJournal);
            return evt;
        }
    }

    public sealed class HostedActor : ReceivePersistentActor
    {
        private readonly List<string> _values = new();
        private IActorRef _snapshotRequester = ActorRefs.Nobody;

        public HostedActor(string persistenceId, string? journalId, string? snapshotId)
        {
            PersistenceId = persistenceId;
            if (journalId is not null)
                JournalPluginId = journalId;
            if (snapshotId is not null)
                SnapshotPluginId = snapshotId;

            Recover<SnapshotOffer>(offer =>
            {
                _values.Clear();
                _values.AddRange(((string)offer.Snapshot).Split(',', StringSplitOptions.RemoveEmptyEntries));
            });
            Recover<string>(value => _values.Add(value));

            Command<string>(value => value == "state", _ => Sender.Tell(string.Join(",", _values)));
            Command<string>(value => value == "snap", _ =>
            {
                _snapshotRequester = Sender;
                SaveSnapshot(string.Join(",", _values));
            });
            Command<SaveSnapshotSuccess>(_ => _snapshotRequester.Tell("snapshot-saved"));
            Command<SaveSnapshotFailure>(failure => _snapshotRequester.Tell("snapshot-failed: " + failure.Cause));
            Command<string>(value =>
            {
                var requester = Sender;
                Persist(value, persisted =>
                {
                    _values.Add(persisted);
                    requester.Tell(persisted);
                });
            });
        }

        public override string PersistenceId { get; }
    }
}
