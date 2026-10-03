//-----------------------------------------------------------------------
// <copyright file="PersistenceSetupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
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
using ConfigurationFactory = Akka.Configuration.ConfigurationFactory;

namespace Akka.Persistence.Tests
{
    /// <summary>
    /// Checks how persistence finds a plugin: a registration for its plugin id first, then the built-in
    /// plugins, then - only if <c>Akka.DynamicTypeLoading</c> is on - reflection on the HOCON <c>class</c>.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class PersistenceSetupSpec : AkkaSpec
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private const string TestAssembly = "Akka.Persistence.Tests";

        private const string JournalPath = "akka.persistence.journal.registered";
        private const string SnapshotPath = "akka.persistence.snapshot-store.registered";
        private const string InMemSnapshotPath = "akka.persistence.snapshot-store.inmem";

        public PersistenceSetupSpec(ITestOutputHelper output) : base(Persistence.DefaultConfig(), output)
        {
        }

        // ---- registered journals and snapshot stores ----

        [Fact(DisplayName = "PersistenceSetup should create a registered journal without a class or reflection When dynamic type loading is off")]
        public async Task Should_create_registered_journal_without_class_or_reflection_When_dynamic_type_loading_is_off()
        {
            var factoryConfig = new TaskCompletionSource<Config>();
            var setup = PersistenceSetup.Create().WithJournal(JournalPath, config =>
                {
                    factoryConfig.TrySetResult(config);
                    return new RegisteredJournal();
                },
                ConfigurationFactory.ParseString("marker = from-default"));

            await RunAsync(false, "", setup, async system =>
            {
                var journal = Persistence.Instance.Apply(system).JournalFor(JournalPath);

                UnderlyingProps(journal).Type.Should().Be(typeof(RegisteredJournal));
                (await factoryConfig.Task.WaitAsync(Timeout)).GetString("marker").Should().Be("from-default", "the factory gets the section with the default config in it");

                // and it is a working journal
                var writer = system.ActorOf(Props.Create(() => new Writer("p-registered", JournalPath, InMemSnapshotPath)));
                (await writer.Ask<string>("evt", Timeout)).Should().Be("evt");
            });
        }

        [Fact(DisplayName = "PersistenceSetup should create a registered snapshot store without a class or reflection When dynamic type loading is off")]
        public async Task Should_create_registered_snapshot_store_without_class_or_reflection_When_dynamic_type_loading_is_off()
        {
            var created = new TaskCompletionSource<Config>();
            var setup = PersistenceSetup.Create().WithSnapshotStore(SnapshotPath, config =>
                {
                    created.TrySetResult(config);
                    return new RegisteredSnapshotStore();
                },
                ConfigurationFactory.ParseString("marker = from-default"));

            await RunAsync(false, "", setup, async system =>
            {
                var snapshotStore = Persistence.Instance.Apply(system).SnapshotStoreFor(SnapshotPath);

                UnderlyingProps(snapshotStore).Type.Should().Be(typeof(RegisteredSnapshotStore));
                (await created.Task.WaitAsync(Timeout)).GetString("marker").Should().Be("from-default");

                var writer = system.ActorOf(Props.Create(() => new Writer("p-snap", "akka.persistence.journal.inmem", SnapshotPath)));
                (await writer.Ask<string>("snap", Timeout)).Should().Be("snapshot-saved");
            });
        }

        [Fact(DisplayName = "PersistenceSetup should put the default config under the HOCON section When the plugin is registered")]
        public async Task Should_put_default_config_under_hocon_section_When_plugin_is_registered()
        {
            var factoryConfig = new TaskCompletionSource<Config>();
            var setup = PersistenceSetup.Create().WithJournal(JournalPath, config =>
                {
                    factoryConfig.TrySetResult(config);
                    return new RegisteredJournal();
                },
                ConfigurationFactory.ParseString("color = default\nsize = default"));
            var hocon = $$"""
                {{JournalPath}} {
                    color = from-hocon
                    plugin-dispatcher = "akka.actor.default-dispatcher"
                }
                """;

            await RunAsync(false, hocon, setup, async system =>
            {
                Persistence.Instance.Apply(system).JournalFor(JournalPath);

                var config = await factoryConfig.Task.WaitAsync(Timeout);
                config.GetString("color").Should().Be("from-hocon", "HOCON beats the default config");
                config.GetString("size").Should().Be("default", "the default config fills what HOCON leaves out");
            });
        }

        [Fact(DisplayName = "PersistenceSetup should ignore the HOCON class When the plugin is registered")]
        public async Task Should_ignore_hocon_class_When_plugin_is_registered()
        {
            var hocon = $$"""
                {{JournalPath}}.class = "Some.Missing.Journal, Some.Missing.Assembly"
                {{SnapshotPath}}.class = "Some.Missing.Store, Some.Missing.Assembly"
                """;
            var setup = PersistenceSetup.Create()
                .WithJournal(JournalPath, _ => new RegisteredJournal())
                .WithSnapshotStore(SnapshotPath, _ => new RegisteredSnapshotStore());

            await RunAsync(false, hocon, setup, system =>
            {
                var persistence = Persistence.Instance.Apply(system);

                UnderlyingProps(persistence.JournalFor(JournalPath)).Type.Should().Be(typeof(RegisteredJournal));
                UnderlyingProps(persistence.SnapshotStoreFor(SnapshotPath)).Type.Should().Be(typeof(RegisteredSnapshotStore));
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistenceSetup should apply the plugin dispatcher and supervisor strategy When the journal is registered")]
        public async Task Should_apply_plugin_dispatcher_and_supervisor_strategy_When_journal_is_registered()
        {
            const string dispatcher = "akka.persistence.dispatchers.default-plugin-dispatcher";
            var hocon = $$"""
                {{JournalPath}} {
                    plugin-dispatcher = "{{dispatcher}}"
                    supervisor-strategy = "Akka.Actor.StoppingSupervisorStrategy"
                }
                """;
            var setup = PersistenceSetup.Create().WithJournal(JournalPath, _ => new RegisteredJournal());

            await RunAsync(false, hocon, setup, system =>
            {
                var props = UnderlyingProps(Persistence.Instance.Apply(system).JournalFor(JournalPath));

                props.Dispatcher.Should().Be(dispatcher);
                props.SupervisorStrategy.Should().BeSameAs(SupervisorStrategy.StoppingStrategy);
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistenceSetup should start several plugins from one setup When registrations are appended")]
        public async Task Should_start_several_plugins_from_one_setup_When_registrations_are_appended()
        {
            const string secondJournalPath = "akka.persistence.journal.second";
            const string secondSnapshotPath = "akka.persistence.snapshot-store.second";
            var setup = PersistenceSetup.Create()
                .WithJournal(JournalPath, _ => new RegisteredJournal())
                .WithJournal(secondJournalPath, _ => new SecondRegisteredJournal())
                .WithSnapshotStore(SnapshotPath, _ => new RegisteredSnapshotStore())
                .WithSnapshotStore(secondSnapshotPath, _ => new SecondRegisteredSnapshotStore());

            await RunAsync(false, "", setup, async system =>
            {
                var persistence = Persistence.Instance.Apply(system);

                UnderlyingProps(persistence.JournalFor(JournalPath)).Type.Should().Be(typeof(RegisteredJournal));
                UnderlyingProps(persistence.JournalFor(secondJournalPath)).Type.Should().Be(typeof(SecondRegisteredJournal));
                UnderlyingProps(persistence.SnapshotStoreFor(SnapshotPath)).Type.Should().Be(typeof(RegisteredSnapshotStore));
                UnderlyingProps(persistence.SnapshotStoreFor(secondSnapshotPath)).Type.Should().Be(typeof(SecondRegisteredSnapshotStore));

                var writer = system.ActorOf(Props.Create(() => new Writer("p-second", secondJournalPath, secondSnapshotPath)));
                (await writer.Ask<string>("evt", Timeout)).Should().Be("evt");
                (await writer.Ask<string>("snap", Timeout)).Should().Be("snapshot-saved");
            });
        }

        [Fact(DisplayName = "PersistencePluginRegistry should let the later registration win the plugin When one id is registered twice")]
        public void Should_let_the_later_registration_win_the_plugin_When_one_id_is_registered_twice()
        {
            var setup = PersistenceSetup.Create()
                .WithJournal(JournalPath, _ => new RegisteredJournal(), ConfigurationFactory.ParseString("a = first\nb = first"))
                .WithJournal(JournalPath, _ => new SecondRegisteredJournal(), ConfigurationFactory.ParseString("a = second"));

            var registry = PersistencePluginRegistry.Build(setup);

            registry.TryGet<JournalDetails>(JournalPath, out var journal).Should().BeTrue();
            journal!.CreateProps(Config.Empty).Type.Should().Be(typeof(SecondRegisteredJournal));
            journal.DefaultConfig!.GetString("a").Should().Be("second");
            journal.DefaultConfig.HasPath("b").Should().BeFalse("the later registration replaces the default config, it does not merge it");
        }

        [Fact(DisplayName = "PersistencePluginRegistry should accumulate the event adapters of a journal When several registrations add them")]
        public void Should_accumulate_the_event_adapters_of_a_journal_When_several_registrations_add_them()
        {
            var setup = PersistenceSetup.Create()
                // an adapter added before its journal is registered
                .WithEventAdapters(JournalPath, [EventAdapterDetails.Create("early", _ => new TagAdapter(), typeof(TaggedEvent))])
                .WithJournal(JournalPath, _ => new RegisteredJournal(), eventAdapters:
                [
                    EventAdapterDetails.Create("own", _ => new WriteOnlyAdapter(), typeof(WriteOnlyEvent)),
                    EventAdapterDetails.Create("clash", _ => new TagAdapter(), typeof(TaggedEvent)),
                ])
                // and one added in a later call, which also replaces the adapter that has its name
                .WithEventAdapters(JournalPath, [EventAdapterDetails.Create("clash", _ => new ReadOnlyAdapter(), typeof(ReadOnlyEvent))])
                .WithEventAdapters("akka.persistence.journal.other", [EventAdapterDetails.Create("elsewhere", _ => new TagAdapter())]);

            var registry = PersistencePluginRegistry.Build(setup);

            registry.EventAdaptersFor(JournalPath).Select(a => a.Name).Should().BeEquivalentTo(new[] { "early", "own", "clash" });
            registry.EventAdaptersFor(JournalPath).Single(a => a.Name == "clash").BoundTypes.Should().BeEquivalentTo(new[] { typeof(ReadOnlyEvent) });
            registry.EventAdaptersFor("akka.persistence.journal.other").Select(a => a.Name).Should().BeEquivalentTo(new[] { "elsewhere" });
            registry.EventAdaptersFor("akka.persistence.journal.unknown").Should().BeEmpty();
        }

        [Fact(DisplayName = "PersistenceSetup should keep the registrations of both sides in order When merging")]
        public void Should_keep_the_registrations_of_both_sides_in_order_When_merging()
        {
            var first = PersistenceSetup.Create().WithJournal(JournalPath, _ => new RegisteredJournal());
            var second = PersistenceSetup.Create()
                .WithSnapshotStore(SnapshotPath, _ => new RegisteredSnapshotStore())
                .WithStashOverflowStrategy(new CustomStashConfigurator());

            var merged = first.Merge(second);

            // With... returns a new setup and leaves the old one as it was
            first.Registrations.Should().HaveCount(1);
            merged.Registrations.Select(r => r.Plugin!.PluginId).Should().Equal(JournalPath, SnapshotPath);
            merged.StashOverflowConfigurator.Should().BeSameAs(second.StashOverflowConfigurator);
            first.Merge(PersistenceSetup.Create()).StashOverflowConfigurator.Should().BeNull();
        }

        [Fact(DisplayName = "PersistenceSetup should add event adapters to a built-in journal When the switch is off")]
        public async Task Should_add_event_adapters_to_a_builtin_journal_When_the_switch_is_off()
        {
            var setup = PersistenceSetup.Create().WithEventAdapters("akka.persistence.journal.inmem",
            [
                EventAdapterDetails.Create("tagger", _ => new TagAdapter(), typeof(TaggedEvent)),
            ]);

            await RunAsync(false, "", setup, system =>
            {
                var adapters = Persistence.Instance.Apply(system).AdaptersFor("akka.persistence.journal.inmem");

                adapters.Get<TaggedEvent>().Should().BeOfType<TagAdapter>();
                UnderlyingProps(Persistence.Instance.Apply(system).JournalFor("akka.persistence.journal.inmem")).Type.Should().Be(typeof(MemoryJournal));
                return Task.CompletedTask;
            });
        }

        // ---- built-ins, the guard and reflection ----

        [Fact(DisplayName = "PersistenceSetup should create the built-in inmem journal and local snapshot store When dynamic type loading is off")]
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
                    UnderlyingProps(persistence.SnapshotStoreFor(InMemSnapshotPath)).Type.Should().Be(typeof(MemorySnapshotStore));
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

        [Fact(DisplayName = "PersistenceSetup should start and use the built-in plugin proxy When dynamic type loading is off")]
        public async Task Should_start_and_use_builtin_plugin_proxy_When_dynamic_type_loading_is_off()
        {
            const string journalProxy = "akka.persistence.journal.proxy";
            const string snapshotProxy = "akka.persistence.snapshot-store.proxy";
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

            await RunAsync(false, hocon, null, async system =>
            {
                var persistence = Persistence.Instance.Apply(system);

                UnderlyingProps(persistence.JournalFor(journalProxy)).Type.Should().Be(typeof(PersistencePluginProxy));
                UnderlyingProps(persistence.SnapshotStoreFor(snapshotProxy)).Type.Should().Be(typeof(PersistencePluginProxy));

                // the proxies start their targets (PreStart asks for them by id) and forward to them
                var writer = system.ActorOf(Props.Create(() => new Writer("p-proxy", journalProxy, snapshotProxy)));
                (await writer.Ask<string>("evt", Timeout)).Should().Be("evt");
                (await writer.Ask<string>("snap", Timeout)).Should().Be("snapshot-saved");
            });
        }

        [Fact(DisplayName = "PersistenceSetup should throw a ConfigurationException naming the setting and the switch When the journal is unregistered and the switch is off")]
        public async Task Should_throw_ConfigurationException_naming_setting_and_switch_When_journal_is_unregistered_and_switch_is_off()
        {
            await RunAsync(false, JournalHocon(typeof(UnregisteredJournal)), null, system =>
            {
                var persistence = Persistence.Instance.Apply(system);

                var exception = Assert.Throws<ConfigurationException>(() => persistence.JournalFor(JournalPath));

                exception.Message.Should().Contain($"[{JournalPath}.class]");
                exception.Message.Should().Contain(typeof(UnregisteredJournal).FullName!);
                exception.Message.Should().Contain("Akka.DynamicTypeLoading");
                exception.Message.Should().Contain("Akka.Persistence.Hosting");
                exception.Message.Should().Contain("WithJournal");
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistenceSetup should throw a ConfigurationException naming the setting and the switch When the snapshot store is unregistered and the switch is off")]
        public async Task Should_throw_ConfigurationException_naming_setting_and_switch_When_snapshot_store_is_unregistered_and_switch_is_off()
        {
            await RunAsync(false, SnapshotHocon(typeof(UnregisteredSnapshotStore)), null, system =>
            {
                var persistence = Persistence.Instance.Apply(system);

                var exception = Assert.Throws<ConfigurationException>(() => persistence.SnapshotStoreFor(SnapshotPath));

                exception.Message.Should().Contain($"[{SnapshotPath}.class]");
                exception.Message.Should().Contain(typeof(UnregisteredSnapshotStore).FullName!);
                exception.Message.Should().Contain("Akka.DynamicTypeLoading");
                exception.Message.Should().Contain("Akka.Persistence.Hosting");
                exception.Message.Should().Contain("WithSnapshot");
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistenceSetup should use reflection When the journal and the snapshot store are unregistered and the switch is on")]
        public async Task Should_use_reflection_When_journal_and_snapshot_store_are_unregistered_and_switch_is_on()
        {
            var hocon = JournalHocon(typeof(UnregisteredJournal)) + SnapshotHocon(typeof(UnregisteredSnapshotStore));

            await RunAsync(true, hocon, null, system =>
            {
                var persistence = Persistence.Instance.Apply(system);

                UnderlyingProps(persistence.JournalFor(JournalPath)).Type.Should().Be(typeof(UnregisteredJournal));
                UnderlyingProps(persistence.SnapshotStoreFor(SnapshotPath)).Type.Should().Be(typeof(UnregisteredSnapshotStore));
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistenceSetup should keep the HOCON class When the plugin is registered and the switch is on")]
        public async Task Should_keep_the_hocon_class_When_the_plugin_is_registered_and_the_switch_is_on()
        {
            // with reflection on nothing changes: HOCON names the type, the registration is not consulted
            var hocon = JournalHocon(typeof(UnregisteredJournal)) + SnapshotHocon(typeof(UnregisteredSnapshotStore));
            var setup = PersistenceSetup.Create()
                .WithJournal(JournalPath, _ => new RegisteredJournal())
                .WithSnapshotStore(SnapshotPath, _ => new RegisteredSnapshotStore());

            await RunAsync(true, hocon, setup, system =>
            {
                var persistence = Persistence.Instance.Apply(system);

                UnderlyingProps(persistence.JournalFor(JournalPath)).Type.Should().Be(typeof(UnregisteredJournal));
                UnderlyingProps(persistence.SnapshotStoreFor(SnapshotPath)).Type.Should().Be(typeof(UnregisteredSnapshotStore));
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistenceSetup should use a registered journal and snapshot store When the switch is on and HOCON names no class")]
        public async Task Should_use_a_registered_journal_and_snapshot_store_When_the_switch_is_on_and_hocon_names_no_class()
        {
            var setup = PersistenceSetup.Create()
                .WithJournal(JournalPath, _ => new RegisteredJournal())
                .WithSnapshotStore(SnapshotPath, _ => new RegisteredSnapshotStore());

            await RunAsync(true, "", setup, system =>
            {
                var persistence = Persistence.Instance.Apply(system);

                UnderlyingProps(persistence.JournalFor(JournalPath)).Type.Should().Be(typeof(RegisteredJournal));
                UnderlyingProps(persistence.SnapshotStoreFor(SnapshotPath)).Type.Should().Be(typeof(RegisteredSnapshotStore));
                return Task.CompletedTask;
            });
        }

        // ---- event adapters ----

        [Fact(DisplayName = "PersistenceSetup should resolve the event adapters and bound types of a registered journal When the switch is off")]
        public async Task Should_resolve_event_adapters_and_bound_types_of_registered_journal_When_switch_is_off()
        {
            await RunAsync(false, "", RegisteredAdapters(), system =>
            {
                var adapters = Persistence.Instance.Apply(system).AdaptersFor(JournalPath);

                adapters.Get<TaggedEvent>().Should().BeOfType<TagAdapter>();
                adapters.Get<SubTaggedEvent>().Should().BeOfType<TagAdapter>("a bound type also binds its subtypes");
                adapters.Get<WriteOnlyEvent>().Should().BeOfType<NoopReadEventAdapter>();
                adapters.Get<ReadOnlyEvent>().Should().BeOfType<NoopWriteEventAdapter>();
                adapters.Get<string>().Should().BeSameAs(IdentityEventAdapter.Instance, "nothing binds string");
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistenceSetup should resolve the event adapters of a registered journal When the switch is on")]
        public async Task Should_resolve_event_adapters_of_registered_journal_When_switch_is_on()
        {
            await RunAsync(true, "", RegisteredAdapters(), system =>
            {
                var adapters = Persistence.Instance.Apply(system).AdaptersFor(JournalPath);

                adapters.Get<TaggedEvent>().Should().BeOfType<TagAdapter>();
                adapters.Get<WriteOnlyEvent>().Should().BeOfType<NoopReadEventAdapter>();
                adapters.Get<ReadOnlyEvent>().Should().BeOfType<NoopWriteEventAdapter>();
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistenceSetup should combine the adapters bound to one event type When they are registered")]
        public async Task Should_combine_adapters_bound_to_one_event_type_When_registered()
        {
            var setup = PersistenceSetup.Create().WithJournal(JournalPath, _ => new RegisteredJournal(), eventAdapters:
            [
                EventAdapterDetails.Create("writer", _ => new WriteOnlyAdapter(), typeof(TaggedEvent)),
                EventAdapterDetails.Create("reader", _ => new ReadOnlyAdapter(), typeof(TaggedEvent)),
            ]);

            await RunAsync(false, "", setup, system =>
            {
                Persistence.Instance.Apply(system).AdaptersFor(JournalPath).Get<TaggedEvent>().Should().BeOfType<ReadWriteEventAdapter>();
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistenceSetup should build an adapter from HOCON When HOCON and a registration share its name and the switch is on")]
        public async Task Should_build_an_adapter_from_hocon_When_hocon_and_a_registration_share_its_name_and_the_switch_is_on()
        {
            var registeredTagger = new TagAdapter();
            var setup = PersistenceSetup.Create().WithJournal(JournalPath, _ => new RegisteredJournal(), eventAdapters:
            [
                EventAdapterDetails.Create("tagger", _ => registeredTagger, typeof(TaggedEvent)),
                EventAdapterDetails.Create("extra", _ => new ReadOnlyAdapter(), typeof(ReadOnlyEvent)),
            ]);
            var hocon = $$"""
                {{JournalPath}} {
                    event-adapters {
                        tagger = "{{typeof(WriteOnlyAdapter).FullName}}, {{TestAssembly}}"
                    }
                    event-adapter-bindings {
                        "{{typeof(WriteOnlyEvent).FullName}}, {{TestAssembly}}" = tagger
                    }
                }
                """;

            await RunAsync(true, hocon, setup, system =>
            {
                var adapters = Persistence.Instance.Apply(system).AdaptersFor(JournalPath);

                // with reflection on HOCON decides what it names, as it always did...
                adapters.Get<WriteOnlyEvent>().Should().BeOfType<NoopReadEventAdapter>("HOCON built `tagger` from WriteOnlyAdapter");
                adapters.Get<TaggedEvent>().Should().BeSameAs(IdentityEventAdapter.Instance, "the registered `tagger` is not used next to HOCON's");
                // ...and a registration adds the adapters HOCON does not name
                adapters.Get<ReadOnlyEvent>().Should().BeOfType<NoopWriteEventAdapter>();
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistenceSetup should use the registered adapter When HOCON and a registration share its name and the switch is off")]
        public async Task Should_use_the_registered_adapter_When_hocon_and_a_registration_share_its_name_and_the_switch_is_off()
        {
            var registeredTagger = new TagAdapter();
            var setup = PersistenceSetup.Create().WithJournal(JournalPath, _ => new RegisteredJournal(), eventAdapters:
            [
                EventAdapterDetails.Create("tagger", _ => registeredTagger, typeof(TaggedEvent)),
            ]);
            var hocon = $$"""
                {{JournalPath}} {
                    event-adapters {
                        tagger = "{{typeof(WriteOnlyAdapter).FullName}}, {{TestAssembly}}"
                    }
                    event-adapter-bindings {
                        "{{typeof(WriteOnlyEvent).FullName}}, {{TestAssembly}}" = tagger
                    }
                }
                """;

            await RunAsync(false, hocon, setup, system =>
            {
                var adapters = Persistence.Instance.Apply(system).AdaptersFor(JournalPath);

                adapters.Get<TaggedEvent>().Should().BeSameAs(registeredTagger, "with reflection off the registration stands in for HOCON");
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistenceSetup should throw naming the event adapter setting When HOCON names an adapter nothing registers and the switch is off")]
        public async Task Should_throw_naming_event_adapter_setting_When_hocon_names_an_adapter_nothing_registers_and_switch_is_off()
        {
            const string notBuiltIn = " is not built in and dynamic type loading is disabled. Use ";
            const string switchText = " or enable the [Akka.DynamicTypeLoading] feature switch.";
            var setup = PersistenceSetup.Create().WithJournal(JournalPath, _ => new RegisteredJournal());

            // an adapter in HOCON that no EventAdapterDetails covers
            var adapterHocon = $$"""
                {{JournalPath}}.event-adapters.tagger = "{{typeof(TagAdapter).FullName}}, {{TestAssembly}}"
                """;
            await RunAsync(false, adapterHocon, setup, system =>
            {
                // through the journal: the setting is named with the plugin path in front
                var exception = Assert.Throws<ConfigurationException>(() => Persistence.Instance.Apply(system).AdaptersFor(JournalPath));

                exception.Message.Should().Contain($"{JournalPath}.event-adapters.tagger");
                exception.Message.Should().Contain("Akka.DynamicTypeLoading");
                exception.Message.Should().Contain("Akka.Persistence.Hosting");

                // through the public overload, which does not know the plugin path: the setting starts at event-adapters
                var relative = Assert.Throws<ConfigurationException>(() => EventAdapters.Create(system, system.Settings.Config.GetConfig(JournalPath)));

                relative.Message.Should().StartWith("[event-adapters.tagger] [" + typeof(TagAdapter).FullName);
                relative.Message.Should().NotContain("event-adapters.event-adapters");
                relative.Message.Should().EndWith(notBuiltIn + "an event adapter added through Akka.Persistence.Hosting (AddEventAdapter on the journal builder)" + switchText);
                return Task.CompletedTask;
            });

            // a HOCON binding for a type no registration lists, next to an adapter that is registered
            var bindingSetup = PersistenceSetup.Create().WithJournal(JournalPath, _ => new RegisteredJournal(), eventAdapters:
            [
                EventAdapterDetails.Create("tagger", _ => new TagAdapter()),
                EventAdapterDetails.Create("writer", _ => new WriteOnlyAdapter()),
            ]);
            var bindingHocon = $$"""
                {{JournalPath}}.event-adapters.other = "{{typeof(ReadOnlyAdapter).FullName}}, {{TestAssembly}}"
                {{JournalPath}}.event-adapter-bindings."{{typeof(TaggedEvent).FullName}}, {{TestAssembly}}" = other
                """;
            await RunAsync(false, bindingHocon, bindingSetup, system =>
            {
                // the adapter `other` is in HOCON only, so it throws first, naming the adapter
                var exception = Assert.Throws<ConfigurationException>(() => Persistence.Instance.Apply(system).AdaptersFor(JournalPath));

                exception.Message.Should().Contain($"{JournalPath}.event-adapters.other");
                return Task.CompletedTask;
            });

            var onlyBindingHocon = $$"""
                {{JournalPath}}.event-adapter-bindings."{{typeof(TaggedEvent).FullName}}, {{TestAssembly}}" = [tagger, unknown-in-hocon]
                """;
            await RunAsync(false, onlyBindingHocon, bindingSetup, system =>
            {
                // `unknown-in-hocon` is not an adapter anywhere, which is an error on every runtime
                Assert.Throws<ArgumentException>(() => Persistence.Instance.Apply(system).AdaptersFor(JournalPath));
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistenceSetup should ignore a HOCON binding that only names registered adapters When the switch is off")]
        public async Task Should_ignore_a_hocon_binding_that_only_names_registered_adapters_When_the_switch_is_off()
        {
            // what Akka.Persistence.Hosting writes next to its registrations: the same adapter and binding in HOCON
            var setup = PersistenceSetup.Create().WithJournal(JournalPath, _ => new RegisteredJournal(), eventAdapters:
            [
                EventAdapterDetails.Create("tagger", _ => new TagAdapter(), typeof(TaggedEvent)),
            ]);
            var hocon = $$"""
                {{JournalPath}}.event-adapters.tagger = "{{typeof(TagAdapter).FullName}}, {{TestAssembly}}"
                {{JournalPath}}.event-adapter-bindings."{{typeof(TaggedEvent).FullName}}, {{TestAssembly}}" = tagger
                """;

            await RunAsync(false, hocon, setup, system =>
            {
                Persistence.Instance.Apply(system).AdaptersFor(JournalPath).Get<TaggedEvent>().Should().BeOfType<TagAdapter>();
                return Task.CompletedTask;
            });
        }

        // ---- stash overflow ----

        [Fact(DisplayName = "PersistenceSetup should resolve the built-in stash overflow configurators When the switch is off")]
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
                exception.Message.Should().Contain("WithStashOverflowStrategy");
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistenceSetup should use a registered stash overflow configurator in place of the HOCON setting When the switch is off or on")]
        public async Task Should_use_registered_stash_overflow_configurator_in_place_of_hocon_setting_When_switch_is_off_or_on()
        {
            var setup = PersistenceSetup.Create().WithStashOverflowStrategy(new CustomStashConfigurator());
            const string hocon = "akka.persistence.internal-stash-overflow-strategy = \"Some.Unknown.Configurator, Some.Assembly\"";

            foreach (var dynamicTypeLoading in new[] { false, true })
            {
                await RunAsync(dynamicTypeLoading, hocon, setup, system =>
                {
                    Persistence.Instance.Apply(system).DefaultInternalStashOverflowStrategy.Should().BeSameAs(CustomStashConfigurator.Strategy);
                    return Task.CompletedTask;
                });
            }

            // and it survives a Merge with a setup that sets none
            await RunAsync(false, hocon, setup.Merge(PersistenceSetup.Create()), system =>
            {
                Persistence.Instance.Apply(system).DefaultInternalStashOverflowStrategy.Should().BeSameAs(CustomStashConfigurator.Strategy);
                return Task.CompletedTask;
            });
        }

        // ---- the records ----

        [Fact(DisplayName = "PersistenceSetup should reject a null argument, a blank id or a repeated adapter name When registering")]
        public void Should_reject_null_argument_blank_id_or_repeated_adapter_name_When_registering()
        {
            PersistenceSetup.Create().Registrations.Should().BeEmpty();

            Assert.Throws<ArgumentNullException>(() => PersistenceSetup.Create().WithJournal<RegisteredJournal>(JournalPath, null!));
            Assert.Throws<ArgumentNullException>(() => PersistenceSetup.Create().WithSnapshotStore<RegisteredSnapshotStore>(SnapshotPath, null!));
            Assert.Throws<ArgumentNullException>(() => PersistenceSetup.Create().WithPlugin(null!));
            Assert.Throws<ArgumentNullException>(() => PersistenceSetup.Create().WithEventAdapters(JournalPath, null!));
            Assert.Throws<ArgumentException>(() => PersistenceSetup.Create().WithEventAdapters(" ", []));
            Assert.Throws<ArgumentNullException>(() => PersistenceSetup.Create().WithStashOverflowStrategy(null!));
            Assert.Throws<ArgumentNullException>(() => PersistenceSetup.Create().Merge(null!));
            Assert.Throws<ArgumentNullException>(() => EventAdapterDetails.Create("tagger", (Func<ExtendedActorSystem, IEventAdapter>)null!));
            Assert.Throws<ArgumentException>(() => JournalDetails.Create(" ", _ => new RegisteredJournal()));
            Assert.Throws<ArgumentException>(() => EventAdapterDetails.Create(" ", _ => new TagAdapter()));
            Assert.Throws<ArgumentException>(() => JournalDetails.Create(JournalPath, _ => new RegisteredJournal(), eventAdapters:
            [
                EventAdapterDetails.Create("tagger", _ => new TagAdapter(), typeof(TaggedEvent)),
                EventAdapterDetails.Create("tagger", _ => new TagAdapter(), typeof(WriteOnlyEvent)),
            ]));
        }

        [Fact(DisplayName = "PersistencePluginDetails should be equal When the plugin ids are equal")]
        public void Should_be_equal_When_plugin_ids_are_equal()
        {
            var journal = JournalDetails.Create(JournalPath, _ => new RegisteredJournal());

            journal.Should().Be(JournalDetails.Create(JournalPath, _ => new SecondRegisteredJournal()));
            journal.GetHashCode().Should().Be(JournalDetails.Create(JournalPath, _ => new SecondRegisteredJournal()).GetHashCode());
            journal.Should().NotBe(JournalDetails.Create(JournalPath + "-other", _ => new RegisteredJournal()));
        }

        // ---- helpers ----

        private static PersistenceSetup RegisteredAdapters() => PersistenceSetup.Create().WithJournal(JournalPath, _ => new RegisteredJournal(), eventAdapters:
        [
            EventAdapterDetails.Create("tagger", _ => new TagAdapter(), typeof(TaggedEvent)),
            EventAdapterDetails.Create("writer", _ => new WriteOnlyAdapter(), typeof(WriteOnlyEvent)),
            EventAdapterDetails.Create("reader", _ => new ReadOnlyAdapter(), typeof(ReadOnlyEvent)),
        ]);

        private static string JournalHocon(Type type) => $$"""
            {{JournalPath}} {
                class = "{{type.FullName}}, {{TestAssembly}}"
                plugin-dispatcher = "akka.actor.default-dispatcher"
            }
            """;

        private static string SnapshotHocon(Type type) => $$"""
            {{SnapshotPath}} {
                class = "{{type.FullName}}, {{TestAssembly}}"
                plugin-dispatcher = "akka.actor.default-dispatcher"
            }
            """;

        private static Props UnderlyingProps(IActorRef actor) => ((ActorRefWithCell)actor).Underlying.Props;

        private async Task RunAsync(bool dynamicTypeLoading, string hocon, PersistenceSetup? setup, Func<ExtendedActorSystem, Task> body)
        {
            var hadSwitch = AppContext.TryGetSwitch(SwitchName, out var previous);
            AppContext.SetSwitch(SwitchName, dynamicTypeLoading);
            try
            {
                var actorSystemSetup = BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(hocon)).And(setup ?? PersistenceSetup.Create());
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
        }

        public sealed class SecondRegisteredJournal : MemoryJournal
        {
        }

        public sealed class UnregisteredJournal : MemoryJournal
        {
        }

        public sealed class RegisteredSnapshotStore : MemorySnapshotStore
        {
        }

        public sealed class UnregisteredSnapshotStore : MemorySnapshotStore
        {
        }

        public sealed class SecondRegisteredSnapshotStore : MemorySnapshotStore
        {
        }

        public class TaggedEvent
        {
        }

        public sealed class SubTaggedEvent : TaggedEvent
        {
        }

        public sealed class WriteOnlyEvent
        {
        }

        public sealed class ReadOnlyEvent
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
