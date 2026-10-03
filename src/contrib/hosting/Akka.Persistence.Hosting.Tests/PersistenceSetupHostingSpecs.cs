// -----------------------------------------------------------------------
//  <copyright file="PersistenceSetupHostingSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
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

    [Theory(DisplayName = "WithInMemoryJournal should create an adapter with a factory When the adapter needs the ActorSystem")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_create_an_adapter_with_a_factory_When_the_adapter_needs_the_ActorSystem(bool dynamicTypeLoading)
    {
        SystemAdapter.Reset();

        await RunAsync(dynamicTypeLoading,
            builder => builder
                .WithInMemoryJournal(journal => journal
                    .AddWriteEventAdapter("system-adapter", system => new SystemAdapter(system), typeof(string)))
                .WithInMemorySnapshotStore(),
            async system =>
            {
                await PersistAndRecoverAsync(system, "p-factory", null, null);

                SystemAdapter.Created.Should().Be(1);
                SystemAdapter.ToJournalCalls.Should().BeGreaterThan(0);
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

    // ---- plugins that supply a factory ----

    [Theory(DisplayName = "WithJournalAndSnapshot should start plugins that supply a factory When there is no HOCON class")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_start_plugins_that_supply_a_factory_When_there_is_no_HOCON_class(bool dynamicTypeLoading)
    {
        WrapAdapter.Reset();
        var journal = new TestJournalOptions("custom", PluginActorFactory.For(_ => new JournalA())) { IsDefaultPlugin = true };
        var snapshot = new TestSnapshotOptions("custom", PluginActorFactory.For(_ => new SnapshotA())) { IsDefaultPlugin = true };

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

    [Theory(DisplayName = "WithJournal should start every plugin When several calls register different plugin ids")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_start_every_plugin_When_several_calls_register_different_plugin_ids(bool dynamicTypeLoading)
    {
        var journalOne = new TestJournalOptions("one", PluginActorFactory.For(_ => new JournalA())) { IsDefaultPlugin = true };
        var journalTwo = new TestJournalOptions("two", PluginActorFactory.For(_ => new JournalB()));
        var snapshotOne = new TestSnapshotOptions("one", PluginActorFactory.For(_ => new SnapshotA())) { IsDefaultPlugin = true };
        var snapshotTwo = new TestSnapshotOptions("two", PluginActorFactory.For(_ => new SnapshotB()));

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
        var first = new TestJournalOptions("same", PluginActorFactory.For(_ => new JournalA())) { IsDefaultPlugin = true };
        var second = new TestJournalOptions("same", PluginActorFactory.For(_ => new JournalB())) { IsDefaultPlugin = true };

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
        var journal = new TestJournalOptions("late", PluginActorFactory.For(_ => new JournalA())) { IsDefaultPlugin = true };

        await RunAsync(dynamicTypeLoading,
            builder =>
            {
                builder.WithJournal(journal).WithInMemorySnapshotStore();
#pragma warning disable CS0618 // the string overload is how an adapter reaches a journal another call configured
                builder.WithJournal("late", j => j.AddEventAdapter<WrapAdapter>("wrap", new[] { typeof(string) }));
#pragma warning restore CS0618
            },
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
        var journal = new TestJournalOptions("early", PluginActorFactory.For(_ => new JournalA())) { IsDefaultPlugin = true };

        await RunAsync(dynamicTypeLoading,
            builder =>
            {
#pragma warning disable CS0618 // the string overload is how an adapter reaches a journal another call configures
                builder.WithJournal("early", j => j.AddEventAdapter<WrapAdapter>("wrap", new[] { typeof(string) }));
#pragma warning restore CS0618
                builder.WithJournal(journal).WithInMemorySnapshotStore();
            },
            async system =>
            {
                PropsOf(Persistence.Instance.Apply(system).JournalFor(null)).Type.Should().Be(typeof(JournalA));

                await PersistAndRecoverAsync(system, "p-early", null, null);
                WrapAdapter.ToJournalCalls.Should().BeGreaterThan(0);
            });
    }

    // ---- read journals and the stash overflow strategy ----

    [Theory(DisplayName = "WithReadJournal should read the events of the default journal When the system starts through Hosting")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_read_the_events_of_the_default_journal_When_the_system_starts_through_Hosting(bool dynamicTypeLoading)
    {
        await RunAsync(dynamicTypeLoading,
            builder => builder.WithInMemoryJournal().WithInMemorySnapshotStore().WithReadJournal(
                InMemoryReadJournal.Identifier,
                static (system, config) => new InMemoryReadJournalProvider(system, config),
                InMemoryReadJournal.DefaultConfiguration().GetConfig(InMemoryReadJournal.Identifier)),
            async system =>
            {
                await PersistAndRecoverAsync(system, "p-query", null, null);

                var readJournal = PersistenceQuery.Get(system).ReadJournalFor<InMemoryReadJournal>(InMemoryReadJournal.Identifier);
                var events = await readJournal
                    .CurrentEventsByPersistenceId("p-query", 0, long.MaxValue)
                    .RunWith(Sink.Seq<EventEnvelope>(), system.Materializer())
                    .WaitAsync(Timeout);

                events.Select(e => e.Event).Should().Equal("a", "b");
            });
    }

    [Theory(DisplayName = "WithStashOverflowStrategy should replace the HOCON setting When the system starts through Hosting")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_replace_the_HOCON_setting_When_the_system_starts_through_Hosting(bool dynamicTypeLoading)
    {
        await RunAsync(dynamicTypeLoading,
            builder => builder.WithInMemoryJournal().WithStashOverflowStrategy(new TestStashConfigurator()),
            system =>
            {
                Persistence.Instance.Apply(system).DefaultInternalStashOverflowStrategy.Should().BeSameAs(TestStashConfigurator.Strategy);
                return Task.CompletedTask;
            });
    }

    // ---- nothing changes for apps that do not register ----

    [Fact(DisplayName = "WithJournal should start a plugin through its HOCON class When it supplies no factory and the switch is on")]
    public async Task Should_start_a_plugin_through_its_HOCON_class_When_it_supplies_no_factory_and_the_switch_is_on()
    {
        var journal = new TestJournalOptions("legacy", null, className: typeof(JournalA).AssemblyQualifiedName!) { IsDefaultPlugin = true };

        await RunAsync(true,
            builder => builder.WithJournal(journal).WithInMemorySnapshotStore(),
            async system =>
            {
                PropsOf(Persistence.Instance.Apply(system).JournalFor(null)).Type.Should().Be(typeof(JournalA));
                await PersistAndRecoverAsync(system, "p-legacy", null, null);
            });
    }

    [Fact(DisplayName = "WithJournal should fail the plugin start and name the Hosting builders When it supplies no factory and the switch is off")]
    public async Task Should_fail_the_plugin_start_and_name_the_Hosting_builders_When_it_supplies_no_factory_and_the_switch_is_off()
    {
        var journal = new TestJournalOptions("legacy", null, className: typeof(JournalA).AssemblyQualifiedName!) { IsDefaultPlugin = true };

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

    [Fact(DisplayName = "WithJournal should reject a null argument When called")]
    public void Should_reject_a_null_argument_When_called()
    {
        var builder = new AkkaConfigurationBuilder(new ServiceCollection(), "null-args");

        Assert.Throws<ArgumentNullException>(() => PluginActorFactory.For<JournalA>(null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithReadJournal<TestProvider>("akka.persistence.query.journal.test", null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithStashOverflowStrategy(null!));
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

    public sealed class TestJournalOptions : JournalOptions
    {
        private readonly PluginActorFactory? _factory;
        private readonly string? _className;

        public TestJournalOptions(string identifier, PluginActorFactory? factory, string? className = null) : base(false)
        {
            Identifier = identifier;
            _factory = factory;
            _className = className;
        }

        public override string Identifier { get; set; }

        protected override Config InternalDefaultConfig => ConfigurationFactory.ParseString(
            $"plugin-dispatcher = \"akka.actor.default-dispatcher\"\n{(_className is null ? "" : $"class = \"{_className}\"")}");

        protected override PluginActorFactory? CreatePluginActorFactory() => _factory;
    }

    public sealed class TestSnapshotOptions : SnapshotOptions
    {
        private readonly PluginActorFactory? _factory;

        public TestSnapshotOptions(string identifier, PluginActorFactory? factory) : base(false)
        {
            Identifier = identifier;
            _factory = factory;
        }

        public override string Identifier { get; set; }

        protected override Config InternalDefaultConfig => ConfigurationFactory.ParseString("plugin-dispatcher = \"akka.actor.default-dispatcher\"");

        protected override PluginActorFactory? CreatePluginActorFactory() => _factory;
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

    public sealed class TestStashConfigurator : IStashOverflowStrategyConfigurator
    {
        public static readonly IStashOverflowStrategy Strategy = new ReplyToStrategy("overflow");

        public IStashOverflowStrategy Create(Config config) => Strategy;
    }

    public sealed class TestProvider : IReadJournalProvider
    {
        public IReadJournal GetReadJournal() => throw new NotSupportedException();
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
