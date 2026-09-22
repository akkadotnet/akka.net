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
        private const string SwitchName = "Akka.DynamicTypeLoading";

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
        /// Runs <paramref name="body"/> with the <c>Akka.DynamicTypeLoading</c> switch forced to
        /// <paramref name="enabled"/> and puts it back afterwards.
        /// </summary>
        /// <remarks>
        /// <see cref="AppContext"/> has no way to unset a switch, so an unset switch is restored as
        /// <c>true</c> - which is the value <see cref="AkkaFeatures.IsDynamicTypeLoadingSupported"/> reports
        /// for an unset switch anyway.
        /// </remarks>
        private static async Task WithDynamicTypeLoading(bool enabled, Func<Task> body)
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
        [InlineData("Akka.Event.SemanticLogMessageFormatter")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter, Akka")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter, Akka, Version=1.5.60.0, Culture=neutral, PublicKeyToken=null")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter, Akka, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null")]
        [InlineData("Akka.Event.SemanticLogMessageFormatter, Akka, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null, ProcessorArchitecture=MSIL, Retargetable=Yes")]
        public void Should_strip_assembly_identity_From_a_qualified_type_name(string typeName)
        {
            // this is what lets a BuiltIn* table carry two keys and still match what Akka.Hosting writes
            Akka.Util.TypeExtensions.StripAssemblyIdentity(typeName).Should()
                .BeOneOf("Akka.Event.SemanticLogMessageFormatter", "Akka.Event.SemanticLogMessageFormatter, Akka");
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
    }
}
