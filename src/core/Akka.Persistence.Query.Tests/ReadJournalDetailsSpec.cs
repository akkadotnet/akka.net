//-----------------------------------------------------------------------
// <copyright file="ReadJournalDetailsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Persistence.Journal;
using FluentAssertions;
using Xunit;
using ConfigurationFactory = Akka.Configuration.ConfigurationFactory;

namespace Akka.Persistence.Query.Tests
{
    /// <summary>
    /// AppContext switches are process-wide, so a spec that flips <c>Akka.DynamicTypeLoading</c> never runs beside another.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class DynamicTypeLoadingCollection
    {
        public const string Name = "Akka.DynamicTypeLoading";
    }

    /// <summary>
    /// Checks how <see cref="PersistenceQuery"/> finds a read journal provider with <c>Akka.DynamicTypeLoading</c>
    /// on and off: a registration for the plugin id, then the guard, then reflection.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class ReadJournalDetailsSpec : TestKit.Xunit.TestKit
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";
        private const string TestAssembly = "Akka.Persistence.Query.Tests";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        public ReadJournalDetailsSpec(ITestOutputHelper output) : base(ConfigurationFactory.Default(), output: output)
        {
        }

        [Fact(DisplayName = "ReadJournalDetails should create a registered read journal without a class When dynamic type loading is off")]
        public async Task Should_create_registered_read_journal_without_a_class_When_switch_is_off()
        {
            var calls = 0;
            var factoryConfig = new TaskCompletionSource<Config>();
            var setup = PersistenceSetup.Create().WithReadJournal(ProviderId, (_, config) =>
                {
                    Interlocked.Increment(ref calls);
                    factoryConfig.TrySetResult(config);
                    return new RegisteredProvider();
                },
                ConfigurationFactory.ParseString("color = default\nsize = default"));

            await RunAsync(false, $"{ProviderId}.color = from-hocon", setup, async system =>
            {
                var readJournal = PersistenceQuery.Get(system).ReadJournalFor<DummyReadJournal>(ProviderId);

                readJournal.Should().BeOfType<DummyReadJournal>();
                calls.Should().Be(1);
                var config = await factoryConfig.Task.WaitAsync(Timeout);
                config.GetString("color").Should().Be("from-hocon", "HOCON beats the default config");
                config.GetString("size").Should().Be("default", "the default config fills what HOCON leaves out");
            });
        }

        [Fact(DisplayName = "ReadJournalDetails should ignore the HOCON class When the read journal is registered")]
        public async Task Should_ignore_hocon_class_When_read_journal_is_registered()
        {
            var setup = PersistenceSetup.Create().WithReadJournal(ProviderId, (_, _) => new RegisteredProvider());

            await RunAsync(false, $"{ProviderId}.class = \"Some.Missing.Provider, Some.Missing.Assembly\"", setup, system =>
            {
                PersistenceQuery.Get(system).ReadJournalFor<DummyReadJournal>(ProviderId).Should().BeOfType<DummyReadJournal>();
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "ReadJournalDetails should not call DefaultConfiguration by reflection When the read journal is registered")]
        public async Task Should_not_call_default_configuration_by_reflection_When_read_journal_is_registered()
        {
            DefaultConfigurationCalls = 0;
            var setup = PersistenceSetup.Create().WithReadJournal(InjectedPath, (_, _) => new ProviderWithDefaultConfigJournal());

            foreach (var dynamicTypeLoading in new[] { false, true })
            {
                await RunAsync(dynamicTypeLoading, "", setup, system =>
                {
                    PersistenceQuery.Get(system).ReadJournalFor<ProviderWithDefaultConfigJournal>(InjectedPath)
                        .Should().BeOfType<ProviderWithDefaultConfigJournal>();

                    DefaultConfigurationCalls.Should().Be(0, "a registered read journal brings its own default config");
                    system.Settings.Config.HasPath(InjectedPath).Should().BeFalse("nothing was injected into the settings");
                    return Task.CompletedTask;
                });
            }
        }

        [Fact(DisplayName = "ReadJournalDetails should throw a ConfigurationException naming the setting When the read journal is unregistered and the switch is off")]
        public async Task Should_throw_ConfigurationException_When_read_journal_is_unregistered_and_switch_is_off()
        {
            await RunAsync(false, ProviderHocon(typeof(RegisteredProvider)), null, system =>
            {
                var query = PersistenceQuery.Get(system);

                var exception = Assert.Throws<ConfigurationException>(() => query.ReadJournalFor<DummyReadJournal>(ProviderId));

                exception.Message.Should().Contain($"[{ProviderId}.class]");
                exception.Message.Should().Contain(typeof(RegisteredProvider).FullName!);
                exception.Message.Should().Contain("Akka.DynamicTypeLoading");
                exception.Message.Should().Contain("Akka.Persistence.Hosting");
                exception.Message.Should().Contain("WithReadJournal");
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "ReadJournalDetails should inject the default configuration by reflection When the read journal is unregistered and the switch is on")]
        public async Task Should_inject_default_configuration_by_reflection_When_read_journal_is_unregistered_and_switch_is_on()
        {
            // nothing registered; the plugin section only exists because the journal type's DefaultConfiguration is injected
            DefaultConfigurationCalls = 0;

            await RunAsync(true, "", null, system =>
            {
                PersistenceQuery.Get(system).ReadJournalFor<ProviderWithDefaultConfigJournal>(InjectedPath)
                    .Should().BeOfType<ProviderWithDefaultConfigJournal>();

                DefaultConfigurationCalls.Should().Be(1);
                system.Settings.Config.HasPath(InjectedPath).Should().BeTrue();
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "ReadJournalDetails should prefer a registered read journal to the HOCON class When the switch is on")]
        public async Task Should_prefer_registered_read_journal_to_hocon_class_When_switch_is_on()
        {
            var calls = 0;
            var setup = PersistenceSetup.Create().WithReadJournal(ProviderId, (_, _) =>
            {
                Interlocked.Increment(ref calls);
                return new RegisteredProvider();
            });

            await RunAsync(true, ProviderHocon(typeof(DummyReadJournalProvider)), setup, system =>
            {
                PersistenceQuery.Get(system).ReadJournalFor<DummyReadJournal>(ProviderId).Should().BeOfType<DummyReadJournal>();
                calls.Should().Be(1);
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "ReadJournalDetails should share one setup with journals and snapshot stores When a read journal is added to a PersistenceSetup")]
        public async Task Should_share_one_setup_with_journals_and_snapshot_stores_When_read_journal_is_added()
        {
            const string journalPath = "akka.persistence.journal.registered";
            var setup = PersistenceSetup.Create()
                .WithJournal(journalPath, _ => new RegisteredJournal())
                .WithReadJournal(ProviderId, (_, _) => new RegisteredProvider());

            setup.Registrations.Should().HaveCount(2);

            await RunAsync(false, "", setup, system =>
            {
                UnderlyingProps(Persistence.Instance.Apply(system).JournalFor(journalPath)).Type.Should().Be(typeof(RegisteredJournal));
                PersistenceQuery.Get(system).ReadJournalFor<DummyReadJournal>(ProviderId).Should().BeOfType<DummyReadJournal>();
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "ReadJournalDetails should reject a null argument or a blank id When registering")]
        public void Should_reject_null_argument_or_blank_id_When_registering()
        {
            Assert.Throws<ArgumentNullException>(() => PersistenceSetup.Create().WithReadJournal<RegisteredProvider>(ProviderId, null!));
            Assert.Throws<ArgumentNullException>(() => ((PersistenceSetup)null!).WithReadJournal(ProviderId, (_, _) => new RegisteredProvider()));
            Assert.Throws<ArgumentException>(() => ReadJournalDetails.Create(" ", (_, _) => new RegisteredProvider()));
            ReadJournalDetails.Create(ProviderId, (_, _) => new RegisteredProvider()).Should().Be(
                ReadJournalDetails.Create(ProviderId, (_, _) => new RegisteredProvider()), "records with one plugin id are equal");
        }

        // ---- helpers ----

        private const string ProviderId = "akka.persistence.query.journal.registered";
        private const string InjectedPath = "akka.persistence.query.journal.from-default-config";

        public static int DefaultConfigurationCalls;

        private static Props UnderlyingProps(IActorRef actor) => ((ActorRefWithCell)actor).Underlying.Props;

        private static string ProviderHocon(Type type) => $$"""
            {{ProviderId}} {
                class = "{{type.FullName}}, {{TestAssembly}}"
                marker = from-hocon
            }
            """;

        private static async Task RunAsync(bool dynamicTypeLoading, string hocon, PersistenceSetup? setup, Func<ExtendedActorSystem, Task> body)
        {
            var hadSwitch = AppContext.TryGetSwitch(SwitchName, out var previous);
            AppContext.SetSwitch(SwitchName, dynamicTypeLoading);
            try
            {
                var actorSystemSetup = BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(hocon)).And(setup ?? PersistenceSetup.Create());
                var system = ActorSystem.Create("query-setup-spec-" + Guid.NewGuid().ToString("N").Substring(0, 8), actorSystemSetup);
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

        public sealed class RegisteredJournal : MemoryJournal
        {
        }

        public sealed class RegisteredProvider : IReadJournalProvider
        {
            public IReadJournal GetReadJournal() => new DummyReadJournal();
        }

        public sealed class ProviderWithDefaultConfigJournal : IReadJournal, IReadJournalProvider
        {
            public static Config DefaultConfiguration()
            {
                Interlocked.Increment(ref DefaultConfigurationCalls);
                return ConfigurationFactory.ParseString($"{InjectedPath} {{ class = \"{typeof(ProviderWithDefaultConfigJournal).FullName}, {TestAssembly}\" }}");
            }

            public IReadJournal GetReadJournal() => this;
        }
    }
}
