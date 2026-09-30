//-----------------------------------------------------------------------
// <copyright file="AkkaFeaturesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Configuration;
using Akka.Event;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Util
{
    /// <summary>
    /// <see cref="AppContext"/> switches are process-wide, so anything that flips
    /// <c>Akka.DynamicTypeLoading</c> belongs in this collection and never runs beside another spec.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class DynamicTypeLoadingCollection
    {
        public const string Name = "Akka.DynamicTypeLoading";
    }

    [Collection(DynamicTypeLoadingCollection.Name)]
    public class AkkaFeaturesSpec
    {
        internal const string SwitchName = "Akka.DynamicTypeLoading";

        /// <summary>
        /// These three types live in Akka.Tests, so none of them is in a <c>BuiltIn*</c> table and the only
        /// way to reach any of them is <see cref="Type.GetType(string)"/>. The scheduler and the formatter
        /// both exist (and are already resolved by name elsewhere in this assembly); the stdout logger name
        /// deliberately does not, because the switch-off path throws before it would resolve anything.
        /// </summary>
        private const string CustomSchedulerTypeName = "Akka.Tests.Actor.TestScheduler, Akka.Tests";

        private const string CustomLogFormatterTypeName =
            "Akka.Tests.Loggers.CustomLogFormatterSpec+CustomLogFormatter, Akka.Tests";

        private const string CustomStdoutLoggerTypeName = "Akka.Tests.Util.NoSuchStdoutLogger, Akka.Tests";

        /// <summary>
        /// Names <see cref="ProbeExtension"/> below - a real, resolvable <see cref="IExtensionId"/> that lives
        /// outside <c>ExtensionsSetup</c>'s first-party table, so it only registers through the
        /// <c>akka.extensions</c> <see cref="Type.GetType(string)"/> fallback.
        /// </summary>
        private const string CustomExtensionTypeName = "Akka.Tests.Util.AkkaFeaturesSpec+ProbeExtension, Akka.Tests";

        /// <summary>
        /// A custom <c>akka.actor.provider</c> name. Never resolved: the switch-off path throws before it
        /// would try, and the switch-on regression test below points at a type that resolves but is not an
        /// <see cref="IActorRefProvider"/>, so it does not need a working provider either.
        /// </summary>
        private const string CustomProviderTypeName = "Akka.Tests.Util.NoSuchProvider, Akka.Tests";

        /// <summary>
        /// Runs <paramref name="body"/> with the <c>Akka.DynamicTypeLoading</c> switch forced to
        /// <paramref name="enabled"/> and puts it back afterwards.
        /// </summary>
        /// <remarks>
        /// <see cref="AppContext"/> has no way to unset a switch, so an unset switch is restored as
        /// <c>true</c> - which is the value <see cref="AkkaFeatures.IsDynamicTypeLoadingSupported"/> reports
        /// for an unset switch anyway.
        /// </remarks>
        internal static async Task WithDynamicTypeLoading(bool enabled, Func<Task> body)
        {
            var hadSwitch = AppContext.TryGetSwitch(SwitchName, out var previous);
            AppContext.SetSwitch(SwitchName, enabled);
            try
            {
                await body();
            }
            finally
            {
                AppContext.SetSwitch(SwitchName, !hadSwitch || previous);
            }
        }

        private static Config ConfigFor(string setting, string typeName)
            => ConfigurationFactory.ParseString($"{setting} = \"{typeName}\"");

        [Fact(DisplayName = "AkkaFeatures should re-read the Akka.DynamicTypeLoading switch on every call")]
        public async Task Should_reread_the_switch_When_it_changes()
        {
            // nothing in Akka.NET or in the test host sets the switch, so an unset switch reports the
            // shipping default. AppContext cannot unset a switch once set, so this has to be asserted
            // before the spec touches it.
            AppContext.TryGetSwitch(SwitchName, out var alreadySet);
            if (!alreadySet)
                AkkaFeatures.IsDynamicTypeLoadingSupported.Should().BeTrue();

            // the property is deliberately not cached, which is what lets these specs flip it at runtime
            await WithDynamicTypeLoading(false, () =>
            {
                AkkaFeatures.IsDynamicTypeLoadingSupported.Should().BeFalse();
                AppContext.SetSwitch(SwitchName, true);
                AkkaFeatures.IsDynamicTypeLoadingSupported.Should().BeTrue();
                return Task.CompletedTask;
            });
        }

        [Theory(DisplayName = "TypeExtensions.StripAssemblyIdentity should reduce an assembly-qualified name to Ns.T, Asm")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter", "Akka.Event.SemanticLogMessageFormatter")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter, Akka", "Akka.Event.SemanticLogMessageFormatter, Akka")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter, Akka, Version=1.5.60.0, Culture=neutral, PublicKeyToken=null", "Akka.Event.SemanticLogMessageFormatter, Akka")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter, Akka, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null", "Akka.Event.SemanticLogMessageFormatter, Akka")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter, Akka, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null, ProcessorArchitecture=MSIL, Retargetable=Yes", "Akka.Event.SemanticLogMessageFormatter, Akka")]
        public void Should_strip_assembly_identity_From_a_qualified_type_name(string typeName, string expected)
        {
            // this is what lets a BuiltIn* table carry two keys and still match what Akka.Hosting writes
            Akka.Util.TypeExtensions.StripAssemblyIdentity(typeName).Should().Be(expected);
        }

        [Theory(DisplayName = "TypeExtensions.TrySplitTypeName should split a type name at the top-level comma")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter", "Akka.Event.SemanticLogMessageFormatter", null)]
        [InlineData("Akka.Event.SemanticLogMessageFormatter, Akka", "Akka.Event.SemanticLogMessageFormatter", "Akka")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter,Akka", "Akka.Event.SemanticLogMessageFormatter", "Akka")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter, akka", "Akka.Event.SemanticLogMessageFormatter", "akka")]
        [InlineData(
            "Akka.Event.SemanticLogMessageFormatter, Akka, Version=1.5.60.0, Culture=neutral, PublicKeyToken=null",
            "Akka.Event.SemanticLogMessageFormatter", "Akka")]
        [InlineData(
            "System.Collections.Generic.Dictionary`2[[System.String, mscorlib],[System.Int32, mscorlib]], mscorlib",
            "System.Collections.Generic.Dictionary`2[[System.String, mscorlib],[System.Int32, mscorlib]]", "mscorlib")]
        public void Should_split_a_type_name_at_the_top_level_comma(string typeName, string expectedName, string? expectedAssembly)
        {
            Akka.Util.TypeExtensions.TrySplitTypeName(typeName, out var name, out var assembly).Should().BeTrue();
            name.Should().Be(expectedName);
            assembly.Should().Be(expectedAssembly);
        }

        [Theory(DisplayName = "TypeExtensions.TrySplitTypeName should reject a null, empty or whitespace-only type name")]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Should_reject_an_empty_type_name(string? typeName)
        {
            Akka.Util.TypeExtensions.TrySplitTypeName(typeName, out _, out _).Should().BeFalse();
        }

        [Theory(DisplayName = "TypeExtensions.ToBuiltInAkkaTypeName should accept the bare name and every spelling of the Akka assembly")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter", "Akka.Event.SemanticLogMessageFormatter")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter, Akka", "Akka.Event.SemanticLogMessageFormatter")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter,Akka", "Akka.Event.SemanticLogMessageFormatter")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter, akka", "Akka.Event.SemanticLogMessageFormatter")]
        [InlineData(
            "Akka.Event.SemanticLogMessageFormatter, Akka, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null",
            "Akka.Event.SemanticLogMessageFormatter")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter, Contoso", null)]
        [InlineData(null, null)]
        [InlineData("", null)]
        [InlineData("   ", null)]
        public void Should_normalize_or_reject_a_type_name_When_computing_the_built_in_Akka_type_name(string? typeName, string? expected)
        {
            Akka.Util.TypeExtensions.ToBuiltInAkkaTypeName(typeName).Should().Be(expected);
        }

        /// <summary>
        /// The explicit switch-ON regression guard: the reflection fallback must still resolve a type that no
        /// <c>BuiltIn*</c> table knows about. Uses the log formatter rather than the scheduler because the
        /// formatter takes no part in shutdown, so the system terminates cleanly afterwards.
        /// </summary>
        [Fact(DisplayName = "Settings should resolve a log formatter named in HOCON when dynamic type loading is on")]
        public async Task Should_resolve_a_custom_log_formatter_When_dynamic_type_loading_is_enabled()
        {
            await WithDynamicTypeLoading(true, async () =>
            {
                var system = ActorSystem.Create(
                    "custom-formatter-on",
                    ConfigFor("akka.logger-formatter", CustomLogFormatterTypeName));
                try
                {
                    system.Settings.LogFormatter.GetType().FullName.Should()
                        .Be("Akka.Tests.Loggers.CustomLogFormatterSpec+CustomLogFormatter");
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }

        [Fact(DisplayName = "ActorSystem should reject a scheduler named in HOCON that is not built in when dynamic type loading is off")]
        public async Task Should_throw_ConfigurationException_When_the_scheduler_is_not_built_in_and_dynamic_type_loading_is_disabled()
            => await AssertRejectsAsync("akka.scheduler.implementation", CustomSchedulerTypeName);

        [Fact(DisplayName = "ActorSystem should reject a log formatter named in HOCON that is not built in when dynamic type loading is off")]
        public async Task Should_throw_ConfigurationException_When_the_log_formatter_is_not_built_in_and_dynamic_type_loading_is_disabled()
            => await AssertRejectsAsync("akka.logger-formatter", CustomLogFormatterTypeName);

        [Fact(DisplayName = "ActorSystem should reject a standard out logger named in HOCON that is not built in when dynamic type loading is off")]
        public async Task Should_throw_ConfigurationException_When_the_stdout_logger_is_not_built_in_and_dynamic_type_loading_is_disabled()
            => await AssertRejectsAsync("akka.stdout-logger-class", CustomStdoutLoggerTypeName);

        [Fact(DisplayName = "ActorSystem should reject a custom actor ref provider that is not built in when dynamic type loading is off")]
        public async Task Should_throw_ConfigurationException_When_the_provider_is_not_built_in_and_dynamic_type_loading_is_disabled()
            => await AssertRejectsAsync("akka.actor.provider", CustomProviderTypeName);

        /// <summary>
        /// The switch-on regression guard for the custom-provider path: <see cref="Settings"/> still resolves
        /// <c>akka.actor.provider</c> by reflection and validates it is an <see cref="IActorRefProvider"/>.
        /// <see cref="string"/> is a real, resolvable type that is not one, so this proves the reflection path
        /// ran without needing a working custom provider.
        /// </summary>
        [Fact(DisplayName = "Settings should still validate a custom actor ref provider by reflection when dynamic type loading is on")]
        public async Task Should_reject_an_invalid_custom_provider_When_dynamic_type_loading_is_enabled()
        {
            await WithDynamicTypeLoading(true, () =>
            {
                var config = ConfigFor("akka.actor.provider", typeof(string).AssemblyQualifiedName!);

                var exception = Assert.Throws<ConfigurationException>(
                    () => ActorSystem.Create("custom-provider-not-a-provider-on", config));

                exception.Message.Should().Contain("is not a valid actor ref provider");
                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// <see cref="ProviderSelection.GetProvider"/> normalizes <c>akka.actor.provider</c> the same way the
        /// <c>BuiltIn*</c> tables do (#8613): strip the assembly identity, split at the comma, compare the
        /// assembly case-insensitively. A spelling that skips these two theories' cases used to fall through
        /// to <see cref="ProviderSelection.Custom"/> - harmless with the switch on (the custom-provider
        /// fallback resolves it by reflection just the same), but with it off that misclassification meant a
        /// working provider name threw <c>NotBuiltIn</c>.
        /// </summary>
        [Theory(DisplayName = "ProviderSelection.GetProvider should classify a spelling variant of a built-in provider the same as its canonical spelling")]
        [InlineData("Akka.Cluster.ClusterActorRefProvider,Akka.Cluster")] // no space after the comma
        [InlineData("Akka.Cluster.ClusterActorRefProvider, Akka.Cluster, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null")] // versioned AQN
        public void Should_classify_a_provider_spelling_variant_As_the_built_in_Cluster_provider(string spelling)
        {
            ProviderSelection.GetProvider(spelling).Should().BeSameAs(ProviderSelection.Cluster.Instance);
        }

        /// <summary>
        /// End-to-end version of the theory above: with the switch off, a misclassified spelling variant used
        /// to throw <c>NotBuiltIn</c> even though the name is one of the three built-ins. Now it reaches
        /// <see cref="ActorSystemImpl.ConfigureProvider"/>'s built-in branch and fails for the ordinary reason
        /// - <c>Akka.Cluster</c>/<c>Akka.Remote</c> is not referenced by <c>Akka.Tests</c> - proving the
        /// classification, not the switch, decided the outcome. Same message either switch state, since
        /// classification does not depend on the switch.
        /// </summary>
        [Theory(DisplayName = "ActorSystem should reject a provider spelling variant for not being referenced, not for being unrecognized, in either switch state")]
        [InlineData(false, "Akka.Cluster.ClusterActorRefProvider,Akka.Cluster", "Akka.Cluster is not referenced by this application")]
        [InlineData(true, "Akka.Cluster.ClusterActorRefProvider,Akka.Cluster", "Akka.Cluster is not referenced by this application")]
        [InlineData(false, "Akka.Cluster.ClusterActorRefProvider, Akka.Cluster, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null", "Akka.Cluster is not referenced by this application")]
        [InlineData(true, "Akka.Cluster.ClusterActorRefProvider, Akka.Cluster, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null", "Akka.Cluster is not referenced by this application")]
        [InlineData(false, "Akka.Remote.RemoteActorRefProvider,Akka.Remote", "Akka.Remote is not referenced by this application")]
        [InlineData(true, "Akka.Remote.RemoteActorRefProvider,Akka.Remote", "Akka.Remote is not referenced by this application")]
        public async Task Should_reject_a_provider_spelling_variant_With_the_providers_own_message(
            bool dynamicTypeLoadingEnabled, string providerClass, string expectedMessageFragment)
        {
            await WithDynamicTypeLoading(dynamicTypeLoadingEnabled, () =>
            {
                var config = ConfigFor("akka.actor.provider", providerClass);

                var exception = Assert.Throws<ConfigurationException>(
                    () => ActorSystem.Create("provider-spelling-variant", config));

                exception.Message.Should().Contain(expectedMessageFragment);
                exception.Message.Should().NotContain("is not built in");
                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// The one built-in provider Akka.Tests can actually boot - Local ships in Akka.dll itself - so this
        /// is the one spelling-variant case that can prove the system comes up cleanly end to end, in either
        /// switch state, instead of only proving which exception it throws.
        /// </summary>
        [Theory(DisplayName = "ActorSystem should boot with a spelling variant of the built-in Local provider in either switch state")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_boot_With_a_local_provider_spelling_variant(bool dynamicTypeLoadingEnabled)
        {
            await WithDynamicTypeLoading(dynamicTypeLoadingEnabled, async () =>
            {
                var config = ConfigFor("akka.actor.provider", "Akka.Actor.LocalActorRefProvider,Akka");
                var system = ActorSystem.Create("local-provider-spelling-variant", config);
                try
                {
                    ((ActorSystemImpl)system).Provider.Should().BeOfType<LocalActorRefProvider>();
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }

        [Fact(DisplayName = "ActorSystem should reject an akka.extensions entry that is not built in when dynamic type loading is off")]
        public async Task Should_throw_ConfigurationException_When_the_extension_is_not_built_in_and_dynamic_type_loading_is_disabled()
        {
            await WithDynamicTypeLoading(false, () =>
            {
                var config = ConfigurationFactory.ParseString($"akka.extensions = [\"{CustomExtensionTypeName}\"]");

                var exception = Assert.Throws<ConfigurationException>(
                    () => ActorSystem.Create("custom-extension-off", config));

                exception.Message.Should().Contain("akka.extensions");
                exception.Message.Should().Contain(CustomExtensionTypeName);
                exception.Message.Should().Contain(SwitchName);
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "ActorSystem should resolve an akka.extensions entry by reflection when dynamic type loading is on")]
        public async Task Should_resolve_a_custom_extension_When_dynamic_type_loading_is_enabled()
        {
            await WithDynamicTypeLoading(true, async () =>
            {
                var config = ConfigurationFactory.ParseString($"akka.extensions = [\"{CustomExtensionTypeName}\"]");
                var system = ActorSystem.Create("custom-extension-on", config);
                try
                {
                    system.HasExtension<ProbeExtensionImpl>().Should().BeTrue();
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }

        /// <summary>
        /// A first-party name (#8648's table) whose assembly this project does not reference - the same one
        /// <c>ExtensionsSetupSpec.Should_resolve_nothing_When_first_party_assembly_is_absent</c> checks at the
        /// table level. #8648 promised this counts as absent, not not-built-in, so it must boot clean in
        /// either switch state instead of throwing <c>NotBuiltIn</c> with the switch off.
        /// </summary>
        [Theory(DisplayName = "ActorSystem should log and skip a first-party extension whose assembly is absent instead of rejecting it as not built in")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_boot_With_a_first_party_extension_Whose_assembly_is_absent(bool dynamicTypeLoadingEnabled)
        {
            await WithDynamicTypeLoading(dynamicTypeLoadingEnabled, async () =>
            {
                var config = ConfigurationFactory.ParseString(
                    "akka.extensions = [\"Akka.DistributedData.DistributedDataProvider, Akka.DistributedData\"]");

                // must not throw - a known first-party name whose module is absent is logged and skipped
                var system = ActorSystem.Create("first-party-extension-absent", config);
                await system.Terminate();
            });
        }

        [Fact(DisplayName = "ActorSystem should still boot on the built-in scheduler and log formatter when dynamic type loading is off")]
        public async Task Should_resolve_the_built_in_types_When_dynamic_type_loading_is_disabled()
        {
            await WithDynamicTypeLoading(false, async () =>
            {
                // the default akka.conf names Akka.Actor.HashedWheelTimerScheduler bare and
                // Akka.Event.SemanticLogMessageFormatter assembly-qualified - both tables have to cover
                // their own spelling for this to boot at all
                var system = ActorSystem.Create("built-ins-off");
                try
                {
                    system.Scheduler.Should().BeOfType<HashedWheelTimerScheduler>();
                    system.Settings.LogFormatter.Should().BeOfType<SemanticLogMessageFormatter>();
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }

        /// <summary>
        /// Proves the normalization the <c>BuiltIn*</c> tables rely on. The tables only carry the bare and
        /// the <c>Ns.T, Akka</c> spelling; Akka.Hosting's <c>LoggerConfigBuilder</c> writes a full
        /// <see cref="Type.AssemblyQualifiedName"/> into HOCON, which matches neither until the lookup has
        /// stripped the assembly identity off it.
        /// </summary>
        [Theory(DisplayName = "ActorSystem should resolve assembly-qualified built-in names when dynamic type loading is off")]
        [InlineData("this build's own version")]
        [InlineData("Version=99.0.0.0")]
        public async Task Should_resolve_fully_assembly_qualified_built_in_names_When_dynamic_type_loading_is_disabled(string label)
        {
            // any version has to match, not just the one this build happens to produce, because the HOCON was
            // very likely written by a differently-versioned Akka.Hosting
            var rewriteVersion = label != "this build's own version";

            await WithDynamicTypeLoading(false, async () =>
            {
                var config = ConfigurationFactory.ParseString($@"
                    akka.stdout-logger-class = ""{Qualified<StandardOutLogger>(rewriteVersion)}""
                    akka.logger-formatter = ""{Qualified<SemanticLogMessageFormatter>(rewriteVersion)}""
                    akka.scheduler.implementation = ""{Qualified<HashedWheelTimerScheduler>(rewriteVersion)}""");

                var system = ActorSystem.Create("assembly-qualified-off", config);
                try
                {
                    system.Settings.StdoutLogger.Should().BeOfType<StandardOutLogger>();
                    system.Settings.LogFormatter.Should().BeOfType<SemanticLogMessageFormatter>();
                    system.Scheduler.Should().BeOfType<HashedWheelTimerScheduler>();
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }

        /// <summary>
        /// The assembly-qualified name of <typeparamref name="T"/>, optionally with its <c>Version=</c>
        /// component rewritten to a version that does not exist, so the value cannot resolve by luck.
        /// </summary>
        private static string Qualified<T>(bool bogusVersion)
        {
            var name = typeof(T).AssemblyQualifiedName!;
            return bogusVersion
                ? Regex.Replace(name, @"Version=[\d.]+", "Version=99.0.0.0")
                : name;
        }

        /// <summary>
        /// Asserts that <paramref name="setting"/> set to <paramref name="typeName"/> fails the boot with a
        /// <see cref="ConfigurationException"/> that names the setting, the value and the switch. Nothing is
        /// constructed on this path, so the name does not have to be resolvable.
        /// </summary>
        private static async Task AssertRejectsAsync(string setting, string typeName)
        {
            await WithDynamicTypeLoading(false, () =>
            {
                var config = ConfigFor(setting, typeName);

                var exception = Assert.Throws<ConfigurationException>(
                    () => ActorSystem.Create("dynamic-type-loading-off", config));

                exception.Message.Should().Contain(setting);
                exception.Message.Should().Contain(typeName);
                exception.Message.Should().Contain(SwitchName);
                return Task.CompletedTask;
            });
        }

        /// <summary>An <see cref="IExtensionId"/> outside <c>ExtensionsSetup</c>'s first-party table, named by
        /// <see cref="CustomExtensionTypeName"/> so the switch-on regression test can prove the
        /// <c>akka.extensions</c> reflection fallback still resolves it.</summary>
        public sealed class ProbeExtension : ExtensionIdProvider<ProbeExtensionImpl>
        {
            public override ProbeExtensionImpl CreateExtension(ExtendedActorSystem system) => new();
        }

        public sealed class ProbeExtensionImpl : IExtension
        {
        }
    }
}
