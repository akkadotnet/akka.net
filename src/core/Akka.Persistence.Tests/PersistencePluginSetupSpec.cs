//-----------------------------------------------------------------------
// <copyright file="PersistencePluginSetupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.Persistence.Snapshot;
using Akka.Persistence.Tests.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using AkkaTypeExtensions = Akka.Util.TypeExtensions;
using ConfigurationFactory = Akka.Configuration.ConfigurationFactory;

namespace Akka.Persistence.Tests
{
    /// <summary>
    /// Checks the lookup order for every persistence type HOCON names - Setup, built-in, guard, reflection -
    /// with <c>Akka.DynamicTypeLoading</c> on and off.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class PersistencePluginSetupSpec : AkkaSpec
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private const string TestAssembly = "Akka.Persistence.Tests";

        private const string JournalPath = "akka.persistence.journal.registered";
        private const string SnapshotPath = "akka.persistence.snapshot-store.registered";

        public PersistencePluginSetupSpec(ITestOutputHelper output) : base(Persistence.DefaultConfig(), output)
        {
        }

        [Fact(DisplayName = "PersistencePluginSetup should create a registered journal without reflection When dynamic type loading is off")]
        public async Task Should_create_registered_journal_without_reflection_When_dynamic_type_loading_is_off()
        {
            var factoryConfig = new TaskCompletionSource<Config>();
            var setup = PersistencePluginSetup.Empty.WithJournal(config =>
            {
                factoryConfig.TrySetResult(config);
                return new RegisteredJournal(config);
            });

            await RunAsync(false, JournalHocon(typeof(RegisteredJournal)), setup, async system =>
            {
                var persistence = Persistence.Instance.Apply(system);
                var journal = persistence.JournalFor(JournalPath);

                UnderlyingProps(journal).Type.Should().Be(typeof(RegisteredJournal));
                var config = await factoryConfig.Task.WaitAsync(Timeout);
                config.GetString("marker").Should().Be("from-hocon", "the factory gets the plugin section");

                // and it is a working journal
                var writer = system.ActorOf(Props.Create(() => new Writer("p-registered", JournalPath, "akka.persistence.snapshot-store.inmem")));
                (await writer.Ask<string>("evt", Timeout)).Should().Be("evt");
            });
        }

        [Fact(DisplayName = "PersistencePluginSetup should create a registered snapshot store When dynamic type loading is off")]
        public async Task Should_create_registered_snapshot_store_When_dynamic_type_loading_is_off()
        {
            var created = new TaskCompletionSource<Config>();
            var setup = PersistencePluginSetup.Empty.WithSnapshotStore(config =>
            {
                created.TrySetResult(config);
                return new RegisteredSnapshotStore();
            });

            await RunAsync(false, SnapshotHocon(typeof(RegisteredSnapshotStore)), setup, async system =>
            {
                var snapshotStore = Persistence.Instance.Apply(system).SnapshotStoreFor(SnapshotPath);

                UnderlyingProps(snapshotStore).Type.Should().Be(typeof(RegisteredSnapshotStore));
                (await created.Task.WaitAsync(Timeout)).GetString("marker").Should().Be("from-hocon");

                var writer = system.ActorOf(Props.Create(() => new Writer("p-snap", "akka.persistence.journal.inmem", SnapshotPath)));
                (await writer.Ask<string>("snap", Timeout)).Should().Be("snapshot-saved");
            });
        }

        [Fact(DisplayName = "PersistencePluginSetup should create the built-in inmem journal and local snapshot store When dynamic type loading is off")]
        public async Task Should_create_builtin_inmem_journal_and_local_snapshot_store_When_dynamic_type_loading_is_off()
        {
            var dir = Path.Combine(Path.GetTempPath(), "akka-persistence-setup-" + Guid.NewGuid().ToString("N"));
            var hocon = $$"""
                akka.persistence.snapshot-store.local.dir = "{{dir.Replace("\\", "/")}}"
                akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.local"
                akka.persistence.journal.shared { class = "Akka.Persistence.Journal.SharedMemoryJournal, Akka.Persistence" }
                akka.persistence.snapshot-store.noop { class = "Akka.Persistence.Snapshot.NoSnapshotStore" }
                """;

            try
            {
                await RunAsync(false, hocon, null, async system =>
                {
                    var persistence = Persistence.Instance.Apply(system);

                    UnderlyingProps(persistence.JournalFor("akka.persistence.journal.inmem")).Type.Should().Be(typeof(MemoryJournal));
                    UnderlyingProps(persistence.JournalFor("akka.persistence.journal.shared")).Type.Should().Be(typeof(SharedMemoryJournal));
                    UnderlyingProps(persistence.SnapshotStoreFor("akka.persistence.snapshot-store.inmem")).Type.Should().Be(typeof(MemorySnapshotStore));
                    UnderlyingProps(persistence.SnapshotStoreFor("akka.persistence.snapshot-store.local")).Type.Should().Be(typeof(LocalSnapshotStore));
                    UnderlyingProps(persistence.SnapshotStoreFor("akka.persistence.snapshot-store.noop")).Type.Should().Be(typeof(NoSnapshotStore));

                    // the built-ins are the real thing, not placeholders
                    var writer = system.ActorOf(Props.Create(() => new Writer("p-builtin", "akka.persistence.journal.inmem", "akka.persistence.snapshot-store.local")));
                    (await writer.Ask<string>("evt", Timeout)).Should().Be("evt");
                    (await writer.Ask<string>("snap", Timeout)).Should().Be("snapshot-saved");
                });
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }

        [Fact(DisplayName = "PersistencePluginSetup should create the built-in plugin proxy When dynamic type loading is off")]
        public async Task Should_create_builtin_plugin_proxy_When_dynamic_type_loading_is_off()
        {
            const string hocon = """
                akka.persistence.journal.proxy {
                    start-target-journal = on
                    target-journal-plugin = "akka.persistence.journal.inmem"
                }
                akka.persistence.snapshot-store.proxy {
                    start-target-snapshot-store = on
                    target-snapshot-store-plugin = "akka.persistence.snapshot-store.inmem"
                }
                """;

            await RunAsync(false, hocon, null, system =>
            {
                var persistence = Persistence.Instance.Apply(system);

                UnderlyingProps(persistence.JournalFor("akka.persistence.journal.proxy")).Type.Should().Be(typeof(PersistencePluginProxy));
                UnderlyingProps(persistence.SnapshotStoreFor("akka.persistence.snapshot-store.proxy")).Type.Should().Be(typeof(PersistencePluginProxy));
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistencePluginSetup should throw a ConfigurationException naming the setting and the switch When the journal is unregistered and the switch is off")]
        public async Task Should_throw_ConfigurationException_naming_setting_and_switch_When_journal_is_unregistered_and_switch_is_off()
        {
            await RunAsync(false, JournalHocon(typeof(UnregisteredJournal)), null, system =>
            {
                var persistence = Persistence.Instance.Apply(system);

                var exception = Assert.Throws<ConfigurationException>(() => persistence.JournalFor(JournalPath));

                exception.Message.Should().Contain($"[{JournalPath}.class]");
                exception.Message.Should().Contain(typeof(UnregisteredJournal).FullName!);
                exception.Message.Should().Contain("Akka.DynamicTypeLoading");
                exception.Message.Should().Contain("PersistencePluginSetup");
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistencePluginSetup should use reflection When the journal is unregistered and the switch is on")]
        public async Task Should_use_reflection_When_journal_is_unregistered_and_switch_is_on()
        {
            await RunAsync(true, JournalHocon(typeof(UnregisteredJournal)), null, system =>
            {
                var journal = Persistence.Instance.Apply(system).JournalFor(JournalPath);

                UnderlyingProps(journal).Type.Should().Be(typeof(UnregisteredJournal));
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistencePluginSetup should call the registered factory instead of reflection When the switch is on")]
        public async Task Should_call_registered_factory_instead_of_reflection_When_switch_is_on()
        {
            var calls = 0;
            var setup = PersistencePluginSetup.Empty.WithJournal(config =>
            {
                calls++;
                return new RegisteredJournal(config);
            });

            await RunAsync(true, JournalHocon(typeof(RegisteredJournal)), setup, async system =>
            {
                var journal = Persistence.Instance.Apply(system).JournalFor(JournalPath);

                var writer = system.ActorOf(Props.Create(() => new Writer("p-factory", JournalPath, "akka.persistence.snapshot-store.inmem")));
                await writer.Ask<string>("evt", Timeout);

                calls.Should().Be(1);
                UnderlyingProps(journal).Type.Should().Be(typeof(RegisteredJournal));
            });
        }

        [Fact(DisplayName = "PersistencePluginSetup should match the class with or without assembly and version When resolving a registration")]
        public async Task Should_match_class_with_or_without_assembly_and_version_When_resolving_registration()
        {
            var name = typeof(RegisteredJournal).FullName!;
            var matching = new[]
            {
                name,
                $"{name}, {TestAssembly}",
                $"{name},{TestAssembly}",
                $"{name}, {TestAssembly.ToUpperInvariant()}",
                $"{name}, {TestAssembly}, Version=1.2.3.4, Culture=neutral, PublicKeyToken=null",
            };
            foreach (var spelling in matching)
                AkkaTypeExtensions.MatchesTypeName(spelling, typeof(RegisteredJournal)).Should().BeTrue(spelling);

            AkkaTypeExtensions.MatchesTypeName($"{name}, Some.Other.Assembly", typeof(RegisteredJournal)).Should().BeFalse();
            AkkaTypeExtensions.MatchesTypeName($"{name}Suffix", typeof(RegisteredJournal)).Should().BeFalse();
            AkkaTypeExtensions.MatchesTypeName(name.ToUpperInvariant(), typeof(RegisteredJournal)).Should().BeFalse("the type name is ordinal");
            AkkaTypeExtensions.MatchesTypeName(null, typeof(RegisteredJournal)).Should().BeFalse();
            AkkaTypeExtensions.MatchesTypeName(" ", typeof(RegisteredJournal)).Should().BeFalse();

            // through a running system: a versioned spelling is a registration hit; another assembly's is not
            var setup = PersistencePluginSetup.Empty.WithJournal(config => new RegisteredJournal(config));
            var versioned = $"{name}, {TestAssembly}, Version=1.2.3.4, Culture=neutral, PublicKeyToken=null";

            await RunAsync(false, JournalHocon(versioned), setup, system =>
            {
                UnderlyingProps(Persistence.Instance.Apply(system).JournalFor(JournalPath)).Type.Should().Be(typeof(RegisteredJournal));
                return Task.CompletedTask;
            });

            await RunAsync(false, JournalHocon($"{name}, Some.Other.Assembly"), setup, system =>
            {
                Assert.Throws<ConfigurationException>(() => Persistence.Instance.Apply(system).JournalFor(JournalPath));
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistencePluginSetup should apply the plugin dispatcher and supervisor strategy When the journal is registered")]
        public async Task Should_apply_plugin_dispatcher_and_supervisor_strategy_When_journal_is_registered()
        {
            const string dispatcher = "akka.persistence.dispatchers.default-plugin-dispatcher";
            var hocon = $$"""
                {{JournalPath}} {
                    class = "{{typeof(RegisteredJournal).FullName}}, {{TestAssembly}}"
                    plugin-dispatcher = "{{dispatcher}}"
                    supervisor-strategy = "Akka.Actor.StoppingSupervisorStrategy"
                }
                """;
            var setup = PersistencePluginSetup.Empty.WithJournal(config => new RegisteredJournal(config));

            await RunAsync(false, hocon, setup, system =>
            {
                var props = UnderlyingProps(Persistence.Instance.Apply(system).JournalFor(JournalPath));

                props.Dispatcher.Should().Be(dispatcher);
                props.SupervisorStrategy.Should().BeSameAs(SupervisorStrategy.StoppingStrategy);
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistencePluginSetup should keep the later registration When merging setups")]
        public async Task Should_keep_later_registration_When_merging_setups()
        {
            var firstCalls = 0;
            var secondCalls = 0;
            var first = PersistencePluginSetup.Empty.WithJournal(config =>
            {
                firstCalls++;
                return new RegisteredJournal(config);
            });
            var second = PersistencePluginSetup.Empty
                .WithJournal(config =>
                {
                    secondCalls++;
                    return new RegisteredJournal(config);
                })
                .WithSnapshotStore(_ => new RegisteredSnapshotStore());

            // With* returns a new instance
            PersistencePluginSetup.Empty.RegisteredTypes.Should().BeEmpty();
            first.RegisteredTypes.Should().BeEquivalentTo(new[] { typeof(RegisteredJournal) });

            var merged = first.Merge(second);

            merged.RegisteredTypes.Should().BeEquivalentTo(new[] { typeof(RegisteredJournal), typeof(RegisteredSnapshotStore) });
            first.RegisteredTypes.Should().HaveCount(1, "Merge leaves its operands alone");

            await RunAsync(false, JournalHocon(typeof(RegisteredJournal)), merged, async system =>
            {
                Persistence.Instance.Apply(system).JournalFor(JournalPath);
                var writer = system.ActorOf(Props.Create(() => new Writer("p-merge", JournalPath, "akka.persistence.snapshot-store.inmem")));
                await writer.Ask<string>("evt", Timeout);
            });

            secondCalls.Should().BeGreaterThan(0);
            firstCalls.Should().Be(0);

            Assert.Throws<ArgumentNullException>(() => PersistencePluginSetup.Empty.WithJournal<RegisteredJournal>(null!));
            Assert.Throws<ArgumentNullException>(() => PersistencePluginSetup.Empty.WithSnapshotStore<RegisteredSnapshotStore>(null!));
            Assert.Throws<ArgumentNullException>(() => PersistencePluginSetup.Empty.WithEventAdapter<TagAdapter>(null!));
            Assert.Throws<ArgumentNullException>(() => PersistencePluginSetup.Empty.WithEventAdapterBinding(null!));
            Assert.Throws<ArgumentNullException>(() => PersistencePluginSetup.Empty.WithStashOverflowStrategy<CustomStashConfigurator>(null!));
            Assert.Throws<ArgumentNullException>(() => PersistencePluginSetup.Empty.Merge(null!));
            Assert.Throws<ArgumentException>(() => PersistencePluginSetup.Empty.WithEventAdapter(_ => new object()));
        }

        [Fact(DisplayName = "PersistencePluginSetup should resolve a registered event adapter and binding When the switch is off")]
        public async Task Should_resolve_registered_event_adapter_and_binding_When_switch_is_off()
        {
            var setup = PersistencePluginSetup.Empty
                .WithEventAdapter(_ => new TagAdapter())
                .WithEventAdapter(_ => new WriteOnlyAdapter())
                .WithEventAdapter(_ => new ReadOnlyAdapter())
                .WithEventAdapterBinding<TaggedEvent>()
                .WithEventAdapterBinding(typeof(WriteOnlyEvent))
                // an adapter type is also a legal binding key
                .WithEventAdapterBinding<ReadOnlyAdapter>();

            await RunAsync(false, AdapterHocon(), setup, system =>
            {
                var adapters = Persistence.Instance.Apply(system).AdaptersFor("akka.persistence.journal.inmem");

                adapters.Get<TaggedEvent>().Should().BeOfType<TagAdapter>();
                adapters.Get<WriteOnlyEvent>().Should().BeOfType<NoopReadEventAdapter>();
                adapters.Get<ReadOnlyAdapter>().Should().BeOfType<NoopWriteEventAdapter>();
                adapters.Get<string>().Should().BeSameAs(IdentityEventAdapter.Instance, "nothing binds string");
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistencePluginSetup should throw naming the event adapter setting When the adapter is unregistered and the switch is off")]
        public async Task Should_throw_naming_event_adapter_setting_When_adapter_is_unregistered_and_switch_is_off()
        {
            // the binding is registered, the adapter is not
            var adapterMissing = PersistencePluginSetup.Empty.WithEventAdapterBinding<TaggedEvent>();

            await RunAsync(false, AdapterHocon(), adapterMissing, system =>
            {
                var exception = Assert.Throws<ConfigurationException>(() => Persistence.Instance.Apply(system).AdaptersFor("akka.persistence.journal.inmem"));

                exception.Message.Should().Contain("akka.persistence.journal.inmem.event-adapters.");
                exception.Message.Should().Contain("Akka.DynamicTypeLoading");
                exception.Message.Should().Contain("PersistencePluginSetup");
                return Task.CompletedTask;
            });

            // the adapters are registered, a binding key is not
            var bindingMissing = PersistencePluginSetup.Empty
                .WithEventAdapter(_ => new TagAdapter())
                .WithEventAdapter(_ => new WriteOnlyAdapter())
                .WithEventAdapter(_ => new ReadOnlyAdapter());

            await RunAsync(false, AdapterHocon(), bindingMissing, system =>
            {
                var exception = Assert.Throws<ConfigurationException>(() => Persistence.Instance.Apply(system).AdaptersFor("akka.persistence.journal.inmem"));

                exception.Message.Should().Contain("akka.persistence.journal.inmem.event-adapter-bindings");
                exception.Message.Should().Contain("WithEventAdapterBinding");
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "EventAdapters.Create should name event-adapters and event-adapter-bindings without a plugin path When called through the public overload")]
        public async Task Should_name_settings_without_plugin_path_When_event_adapters_are_created_through_the_public_overload()
        {
            const string notBuiltIn = " is not built in and dynamic type loading is disabled. Use ";
            const string switchText = " or enable the [Akka.DynamicTypeLoading] feature switch.";

            // the adapter is unregistered: the setting reads event-adapters.<name>
            var bindingOnly = PersistencePluginSetup.Empty.WithEventAdapterBinding<TaggedEvent>();
            await RunAsync(false, AdapterHocon(), bindingOnly, system =>
            {
                var config = system.Settings.Config.GetConfig("akka.persistence.journal.inmem");

                var exception = Assert.Throws<ConfigurationException>(() => EventAdapters.Create(system, config));

                exception.Message.Should().StartWith("[event-adapters.");
                exception.Message.Should().Contain("] [" + typeof(TagAdapter).FullName);
                exception.Message.Should().NotContain("event-adapters.event-adapters");
                exception.Message.Should().EndWith(notBuiltIn + "an event adapter registered through PersistencePluginSetup" + switchText);
                return Task.CompletedTask;
            });

            // the adapters are registered, a binding key is not: the setting reads event-adapter-bindings
            var adaptersOnly = PersistencePluginSetup.Empty
                .WithEventAdapter(_ => new TagAdapter())
                .WithEventAdapter(_ => new WriteOnlyAdapter())
                .WithEventAdapter(_ => new ReadOnlyAdapter());
            await RunAsync(false, AdapterHocon(), adaptersOnly, system =>
            {
                var config = system.Settings.Config.GetConfig("akka.persistence.journal.inmem");

                var exception = Assert.Throws<ConfigurationException>(() => EventAdapters.Create(system, config));

                exception.Message.Should().StartWith("[event-adapter-bindings] [");
                exception.Message.Should().EndWith(notBuiltIn + "an event type registered through PersistencePluginSetup.WithEventAdapterBinding" + switchText);
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistencePluginSetup should resolve the built-in stash overflow configurators When the switch is off")]
        public async Task Should_resolve_builtin_stash_overflow_configurators_When_switch_is_off()
        {
            await RunAsync(false, "", null, system =>
            {
                Persistence.Instance.Apply(system).DefaultInternalStashOverflowStrategy.Should().BeSameAs(ThrowOverflowExceptionStrategy.Instance);
                return Task.CompletedTask;
            });

            foreach (var spelling in new[] { "Akka.Persistence.DiscardConfigurator", "Akka.Persistence.DiscardConfigurator, Akka.Persistence" })
            {
                await RunAsync(false, $"akka.persistence.internal-stash-overflow-strategy = \"{spelling}\"", null, system =>
                {
                    Persistence.Instance.Apply(system).DefaultInternalStashOverflowStrategy.Should().BeSameAs(DiscardToDeadLetterStrategy.Instance);
                    return Task.CompletedTask;
                });
            }

            await RunAsync(false, "akka.persistence.internal-stash-overflow-strategy = \"Some.Unknown.Configurator, Some.Assembly\"", null, system =>
            {
                var exception = Assert.Throws<ConfigurationException>(() => Persistence.Instance.Apply(system).DefaultInternalStashOverflowStrategy);

                exception.Message.Should().Contain("[akka.persistence.internal-stash-overflow-strategy]");
                exception.Message.Should().Contain("Akka.DynamicTypeLoading");
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistencePluginSetup should resolve a registered stash overflow configurator When the switch is off")]
        public async Task Should_resolve_registered_stash_overflow_configurator_When_switch_is_off()
        {
            var setup = PersistencePluginSetup.Empty.WithStashOverflowStrategy(() => new CustomStashConfigurator());
            var hocon = $"akka.persistence.internal-stash-overflow-strategy = \"{typeof(CustomStashConfigurator).FullName}, {TestAssembly}\"";

            await RunAsync(false, hocon, setup, system =>
            {
                Persistence.Instance.Apply(system).DefaultInternalStashOverflowStrategy.Should().BeSameAs(CustomStashConfigurator.Strategy);
                return Task.CompletedTask;
            });
        }

        // ---- helpers ----

        private static string JournalHocon(Type type) => JournalHocon($"{type.FullName}, {TestAssembly}");

        private static string JournalHocon(string className) => $$"""
            {{JournalPath}} {
                class = "{{className}}"
                plugin-dispatcher = "akka.actor.default-dispatcher"
                marker = from-hocon
            }
            """;

        private static string SnapshotHocon(Type type) => $$"""
            {{SnapshotPath}} {
                class = "{{type.FullName}}, {{TestAssembly}}"
                plugin-dispatcher = "akka.actor.default-dispatcher"
                marker = from-hocon
            }
            """;

        private static string AdapterHocon() => $$"""
            akka.persistence.journal.inmem {
                event-adapters {
                    tagger = "{{typeof(TagAdapter).FullName}}, {{TestAssembly}}"
                    writer = "{{typeof(WriteOnlyAdapter).FullName}}, {{TestAssembly}}"
                    reader = "{{typeof(ReadOnlyAdapter).FullName}}, {{TestAssembly}}"
                }
                event-adapter-bindings {
                    "{{typeof(TaggedEvent).FullName}}, {{TestAssembly}}" = tagger
                    "{{typeof(WriteOnlyEvent).FullName}}, {{TestAssembly}}" = writer
                    "{{typeof(ReadOnlyAdapter).FullName}}, {{TestAssembly}}" = reader
                }
            }
            """;

        private static Props UnderlyingProps(IActorRef actor) => ((ActorRefWithCell)actor).Underlying.Props;

        private async Task RunAsync(bool dynamicTypeLoading, string hocon, PersistencePluginSetup? setup, Func<ExtendedActorSystem, Task> body)
        {
            var hadSwitch = AppContext.TryGetSwitch(SwitchName, out var previous);
            AppContext.SetSwitch(SwitchName, dynamicTypeLoading);
            try
            {
                var actorSystemSetup = BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(hocon)).And(setup ?? PersistencePluginSetup.Empty);
                var system = ActorSystem.Create("setup-spec-" + Guid.NewGuid().ToString("N").Substring(0, 8), actorSystemSetup);
                try
                {
                    await body((ExtendedActorSystem)system);
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

        // ---- plugin and event types ----

        public sealed class RegisteredJournal : MemoryJournal
        {
            public RegisteredJournal(Config config)
            {
            }
        }

        public sealed class UnregisteredJournal : MemoryJournal
        {
        }

        public sealed class RegisteredSnapshotStore : MemorySnapshotStore
        {
        }

        public sealed class TaggedEvent
        {
        }

        public sealed class WriteOnlyEvent
        {
        }

        public sealed class TagAdapter : IEventAdapter
        {
            public string Manifest(object evt) => "tagged";

            public object ToJournal(object evt) => evt;

            public IEventSequence FromJournal(object evt, string manifest) => EventSequence.Single(evt);
        }

        public sealed class WriteOnlyAdapter : IWriteEventAdapter
        {
            public string Manifest(object evt) => "write-only";

            public object ToJournal(object evt) => evt;
        }

        public sealed class ReadOnlyAdapter : IReadEventAdapter
        {
            public IEventSequence FromJournal(object evt, string manifest) => EventSequence.Single(evt);
        }

        public sealed class CustomStashConfigurator : IStashOverflowStrategyConfigurator
        {
            public static readonly IStashOverflowStrategy Strategy = new ReplyToStrategy("overflow");

            public IStashOverflowStrategy Create(Config config) => Strategy;
        }

        private sealed class Writer : UntypedPersistentActor
        {
            public Writer(string persistenceId, string journalId, string snapshotId)
            {
                PersistenceId = persistenceId;
                JournalPluginId = journalId;
                SnapshotPluginId = snapshotId;
            }

            public override string PersistenceId { get; }

            protected override void OnRecover(object message)
            {
            }

            protected override void OnCommand(object message)
            {
                switch (message)
                {
                    case "evt":
                        var replyTo = Sender;
                        Persist("evt", e => replyTo.Tell(e));
                        break;
                    case "snap":
                        _snapshotRequester = Sender;
                        SaveSnapshot("state");
                        break;
                    case SaveSnapshotSuccess:
                        _snapshotRequester.Tell("snapshot-saved");
                        break;
                    case SaveSnapshotFailure failure:
                        _snapshotRequester.Tell("snapshot-failed: " + failure.Cause);
                        break;
                }
            }

            private IActorRef _snapshotRequester = ActorRefs.Nobody;
        }
    }
}
