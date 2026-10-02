//-----------------------------------------------------------------------
// <copyright file="PersistenceQuerySetupSpec.cs" company="Akka.NET Project">
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
    /// on and off: Setup, guard, reflection.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class PersistenceQuerySetupSpec : TestKit.Xunit.TestKit
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";
        private const string TestAssembly = "Akka.Persistence.Query.Tests";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        public PersistenceQuerySetupSpec(ITestOutputHelper output) : base(ConfigurationFactory.Default(), output: output)
        {
        }

        [Fact(DisplayName = "PersistenceQuerySetup should create a registered read journal When dynamic type loading is off")]
        public async Task Should_create_registered_read_journal_When_switch_is_off()
        {
            var calls = 0;
            var setup = PersistenceQuerySetup.Empty.WithReadJournal((system, config) =>
            {
                Interlocked.Increment(ref calls);
                config.GetString("marker").Should().Be("from-hocon", "the factory gets the plugin section");
                return new RegisteredProvider();
            });

            await RunAsync(false, ProviderHocon(typeof(RegisteredProvider)), setup, system =>
            {
                var readJournal = PersistenceQuery.Get(system).ReadJournalFor<DummyReadJournal>(ProviderId);

                readJournal.Should().BeOfType<DummyReadJournal>();
                calls.Should().Be(1);
                setup.RegisteredTypes.Should().BeEquivalentTo(new[] { typeof(RegisteredProvider) });
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistenceQuerySetup should not inject the default configuration by reflection When dynamic type loading is off")]
        public async Task Should_not_inject_default_configuration_by_reflection_When_switch_is_off()
        {
            var setup = PersistenceQuerySetup.Empty.WithReadJournal((_, _) => new ProviderWithDefaultConfig());
            DefaultConfigurationCalls = 0;

            await RunAsync(false, ProviderHocon(typeof(ProviderWithDefaultConfig)), setup, system =>
            {
                PersistenceQuery.Get(system).ReadJournalFor<DummyReadJournal>(ProviderId).Should().BeOfType<DummyReadJournal>();

                DefaultConfigurationCalls.Should().Be(0);
                system.Settings.Config.HasPath(InjectedPath).Should().BeFalse();
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistenceQuerySetup should throw a ConfigurationException When the read journal is unregistered and the switch is off")]
        public async Task Should_throw_ConfigurationException_When_read_journal_is_unregistered_and_switch_is_off()
        {
            await RunAsync(false, ProviderHocon(typeof(RegisteredProvider)), null, system =>
            {
                var query = PersistenceQuery.Get(system);

                var exception = Assert.Throws<ConfigurationException>(() => query.ReadJournalFor<DummyReadJournal>(ProviderId));

                exception.Message.Should().Contain($"[{ProviderId}.class]");
                exception.Message.Should().Contain(typeof(RegisteredProvider).FullName!);
                exception.Message.Should().Contain("Akka.DynamicTypeLoading");
                exception.Message.Should().Contain("PersistenceQuerySetup");
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistenceQuerySetup should inject the default configuration by reflection When the switch is on")]
        public async Task Should_inject_default_configuration_by_reflection_When_switch_is_on()
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

        [Fact(DisplayName = "PersistenceQuerySetup should prefer a registered provider to reflection and still inject the default configuration When the switch is on")]
        public async Task Should_prefer_registered_provider_and_still_inject_default_configuration_When_switch_is_on()
        {
            // the registered provider hands out ProviderWithDefaultConfigJournal, whose DefaultConfiguration names
            // that same type as the plugin's class, so the registration is what matches it
            DefaultConfigurationCalls = 0;
            var calls = 0;
            var setup = PersistenceQuerySetup.Empty.WithReadJournal<ProviderWithDefaultConfigJournal>((_, _) =>
            {
                Interlocked.Increment(ref calls);
                return new ProviderWithDefaultConfigJournal();
            });

            await RunAsync(true, "", setup, system =>
            {
                PersistenceQuery.Get(system).ReadJournalFor<ProviderWithDefaultConfigJournal>(InjectedPath)
                    .Should().BeOfType<ProviderWithDefaultConfigJournal>();

                calls.Should().Be(1, "the registered factory built the provider");
                DefaultConfigurationCalls.Should().Be(1);
                system.Settings.Config.HasPath(InjectedPath).Should().BeTrue();
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "PersistenceQuerySetup should hold one registration per type and reject null When merging and registering")]
        public void Should_hold_one_registration_per_type_and_reject_null_When_merging_and_registering()
        {
            var first = PersistenceQuerySetup.Empty.WithReadJournal((_, _) => new RegisteredProvider());
            var second = PersistenceQuerySetup.Empty.WithReadJournal((_, _) => new RegisteredProvider());

            first.Merge(second).RegisteredTypes.Should().HaveCount(1);
            PersistenceQuerySetup.Empty.RegisteredTypes.Should().BeEmpty();
            Assert.Throws<ArgumentNullException>(() => PersistenceQuerySetup.Empty.WithReadJournal<RegisteredProvider>(null!));
            Assert.Throws<ArgumentNullException>(() => PersistenceQuerySetup.Empty.Merge(null!));
        }

        // ---- helpers ----

        private const string ProviderId = "akka.persistence.query.journal.registered";
        private const string InjectedPath = "akka.persistence.query.journal.from-default-config";

        public static int DefaultConfigurationCalls;

        private static string ProviderHocon(Type type) => $$"""
            {{ProviderId}} {
                class = "{{type.FullName}}, {{TestAssembly}}"
                marker = from-hocon
            }
            """;

        private static async Task RunAsync(bool dynamicTypeLoading, string hocon, PersistenceQuerySetup? setup, Func<ExtendedActorSystem, Task> body)
        {
            var hadSwitch = AppContext.TryGetSwitch(SwitchName, out var previous);
            AppContext.SetSwitch(SwitchName, dynamicTypeLoading);
            try
            {
                var actorSystemSetup = BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(hocon)).And(setup ?? PersistenceQuerySetup.Empty);
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

        public sealed class RegisteredProvider : IReadJournalProvider
        {
            public IReadJournal GetReadJournal() => new DummyReadJournal();
        }

        public sealed class ProviderWithDefaultConfig : IReadJournalProvider
        {
            public static Config DefaultConfiguration()
            {
                Interlocked.Increment(ref DefaultConfigurationCalls);
                return ConfigurationFactory.ParseString($"{InjectedPath} {{ class = \"{typeof(ProviderWithDefaultConfig).FullName}, {TestAssembly}\" }}");
            }

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
