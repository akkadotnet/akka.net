//-----------------------------------------------------------------------
// <copyright file="BuiltInLoggersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Tests.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Event
{
    /// <summary>
    /// A logger that lives in Akka.Tests - so it is not in <c>LoggingBus.BuiltInDefaultLoggerNames</c> or either of
    /// its siblings, and can only be reached by name - and which reports that it was constructed and initialized.
    /// </summary>
    public sealed class CountingTestLogger : DefaultLogger
    {
        /// <summary>
        /// Completed by the logger actor once it has answered <see cref="InitializeLogger"/>. A test that starts a
        /// system using this logger replaces it first, so a stale result from an earlier system cannot satisfy it.
        /// </summary>
        public static TaskCompletionSource<bool> Initialized { get; set; } = NewCompletionSource();

        public static TaskCompletionSource<bool> NewCompletionSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override bool Receive(object message)
        {
            var handled = base.Receive(message);
            if (message is InitializeLogger)
                Initialized.TrySetResult(true);
            return handled;
        }
    }

    /// <summary>
    /// Covers the <c>akka.loggers</c> resolution in <see cref="LoggingBus"/>: the three loggers Akka.dll ships
    /// resolve from the built-in name tables under every spelling HOCON uses, and anything else needs dynamic
    /// type loading.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class BuiltInLoggersSpec
    {
        private const string CustomLoggerTypeName = "Akka.Tests.Event.CountingTestLogger";

        private const string CustomLoggerConfig =
            "akka.loggers = [\"" + CustomLoggerTypeName + ", Akka.Tests\"]";

        /// <summary>
        /// Every spelling that has to reach <see cref="DefaultLogger"/>: the two keys
        /// <c>LoggingBus.BuiltInDefaultLoggerNames</c> carries - the bare name core's own akka.conf ships and the
        /// <c>"Ns.T, Akka"</c> form - plus the two assembly-qualified names that only match because the lookup
        /// strips assembly identity first. <c>ForeignVersion</c> is the load-bearing one: it names a version this
        /// build is not, which is what Akka.Hosting HOCON written against another Akka version looks like.
        /// </summary>
        public static IEnumerable<object[]> DefaultLoggerSpellings()
        {
            yield return new object[] { "Akka.Event.DefaultLogger" };
            yield return new object[] { "Akka.Event.DefaultLogger, Akka" };
            yield return new object[] { typeof(DefaultLogger).AssemblyQualifiedName! };
            yield return new object[] { ForeignVersion("Akka.Event.DefaultLogger") };
        }

        /// <summary>
        /// <c>LoggingBus.BuiltInTraceLoggerNames</c>. <see cref="TraceLogger"/>'s own documentation tells users to
        /// write the <c>"Ns.T, Akka"</c> spelling, so all three have to resolve.
        /// </summary>
        public static IEnumerable<object[]> TraceLoggerSpellings()
        {
            yield return new object[] { "Akka.Event.TraceLogger" };
            yield return new object[] { "Akka.Event.TraceLogger, Akka" };
            yield return new object[] { typeof(TraceLogger).AssemblyQualifiedName! };
            yield return new object[] { ForeignVersion("Akka.Event.TraceLogger") };
        }

        /// <summary>
        /// <c>LoggingBus.BuiltInStandardOutLoggerNames</c>. <see cref="StandardOutLogger"/> is a
        /// <see cref="MinimalLogger"/>, so <c>StartDefaultLoggers</c> takes the skip branch and starts no actor -
        /// but the name still has to resolve, which is what <c>LoggerSpec</c> relies on.
        /// </summary>
        public static IEnumerable<object[]> StandardOutLoggerSpellings()
        {
            yield return new object[] { "Akka.Event.StandardOutLogger" };
            yield return new object[] { "Akka.Event.StandardOutLogger, Akka" };
            yield return new object[] { typeof(StandardOutLogger).AssemblyQualifiedName! };
            yield return new object[] { ForeignVersion("Akka.Event.StandardOutLogger") };
        }

        /// <summary>
        /// An assembly-qualified name for <paramref name="typeName"/> that pins a version, culture and public key
        /// token this build does not have - so it can only resolve if the lookup strips assembly identity rather
        /// than comparing against <c>typeof(T).AssemblyQualifiedName</c>.
        /// </summary>
        private static string ForeignVersion(string typeName)
            => $"{typeName}, Akka, Version=99.0.0.0, Culture=neutral, PublicKeyToken=1234567890abcdef";

        [Theory(DisplayName = "LoggingBus should start the built-in default logger when dynamic type loading is off")]
        [MemberData(nameof(DefaultLoggerSpellings))]
        public async Task Should_start_the_built_in_default_logger_When_dynamic_type_loading_is_disabled(string spelling)
            => await AssertLoggerStarts(spelling, nameof(DefaultLogger));

        [Theory(DisplayName = "LoggingBus should start the built-in trace logger when dynamic type loading is off")]
        [MemberData(nameof(TraceLoggerSpellings))]
        public async Task Should_start_the_built_in_trace_logger_When_dynamic_type_loading_is_disabled(string spelling)
            => await AssertLoggerStarts(spelling, nameof(TraceLogger));

        [Theory(DisplayName = "LoggingBus should accept the built-in standard out logger when dynamic type loading is off")]
        [MemberData(nameof(StandardOutLoggerSpellings))]
        public async Task Should_accept_the_built_in_standard_out_logger_When_dynamic_type_loading_is_disabled(string spelling)
        {
            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, async () =>
            {
                var config = ConfigurationFactory.ParseString($"akka.loggers = [\"{spelling}\"]");

                // a MinimalLogger is never started as an actor, so the check is that the name resolved:
                // with the switch off an unresolved akka.loggers entry makes ActorSystem.Create throw
                var system = ActorSystem.Create("built-in-stdout-logger-off", config);
                await system.Terminate();
            });
        }

        /// <summary>
        /// The switch-ON regression guard: a logger that is not built in must keep resolving by name exactly as it
        /// does today, which is what every application that names its own logger depends on.
        /// </summary>
        [Fact(DisplayName = "LoggingBus should resolve a logger named in HOCON when dynamic type loading is on")]
        public async Task Should_resolve_a_custom_logger_When_dynamic_type_loading_is_enabled()
        {
            await AkkaFeaturesSpec.WithDynamicTypeLoading(true, async () =>
            {
                CountingTestLogger.Initialized = CountingTestLogger.NewCompletionSource();

                var system = ActorSystem.Create("custom-logger-on", ConfigurationFactory.ParseString(CustomLoggerConfig));
                try
                {
                    await CountingTestLogger.Initialized.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }

        [Fact(DisplayName = "LoggingBus should reject a logger named in HOCON that is not built in when dynamic type loading is off")]
        public async Task Should_throw_ConfigurationException_When_the_logger_is_not_built_in_and_dynamic_type_loading_is_disabled()
        {
            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, () =>
            {
                var config = ConfigurationFactory.ParseString(CustomLoggerConfig);

                var exception = Assert.Throws<ConfigurationException>(
                    () => ActorSystem.Create("custom-logger-off", config));

                exception.Message.Should().Contain("akka.loggers");
                exception.Message.Should().Contain(CustomLoggerTypeName);
                exception.Message.Should().Contain(AkkaFeaturesSpec.SwitchName);
                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// Boots a system on <paramref name="spelling"/> with the switch off and resolves the logger actor the
        /// logging bus started, which is what proves the name reached <c>Props.Create</c> and an actor came out.
        /// </summary>
        /// <remarks>
        /// <c>LoggingBus.CreateLoggerName</c> numbers loggers from a process-global counter, so the name cannot be
        /// predicted - hence the wildcard selection rather than a hard-coded <c>log1-</c>.
        /// </remarks>
        private static async Task AssertLoggerStarts(string spelling, string loggerTypeName)
        {
            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, async () =>
            {
                var config = ConfigurationFactory.ParseString($"akka.loggers = [\"{spelling}\"]");

                var system = ActorSystem.Create("built-in-logger-off", config);
                try
                {
                    var logger = await system.ActorSelection($"/system/log*-{loggerTypeName}")
                        .ResolveOne(TimeSpan.FromSeconds(3));

                    logger.Path.Name.Should().EndWith($"-{loggerTypeName}");
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }
    }
}
