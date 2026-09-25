//-----------------------------------------------------------------------
// <copyright file="MailboxFeatureSwitchSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.Tests.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Dispatch
{
    /// <summary>
    /// Covers <see cref="Mailboxes"/> against the <c>Akka.DynamicTypeLoading</c> feature switch: everything
    /// core's own <c>akka.conf</c> names has to resolve with the switch off, and anything that does not ship
    /// inside Akka.dll has to resolve with it on and be refused with it off.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public sealed class MailboxFeatureSwitchSpec
    {
        private readonly ITestOutputHelper _output;

        public MailboxFeatureSwitchSpec(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// <see cref="TestPriorityMailbox"/> lives in Akka.Tests, so it is by definition not one of the mailbox
        /// types built into Akka.dll - the only way to reach it is <see cref="Type.GetType(string)"/>.
        /// </summary>
        private const string CustomMailboxTypeName = "Akka.Tests.Dispatch.TestPriorityMailbox";

        private const string CustomMailboxConfig = @"
            custom-mailbox {
                mailbox-type = """ + CustomMailboxTypeName + @", Akka.Tests""
            }";

        /// <summary>
        /// Every mailbox id core resolves on a default boot, mapped to the <see cref="MailboxType"/> its
        /// <c>mailbox-type</c> names.
        /// </summary>
        private static readonly IReadOnlyList<(string Id, Type Expected)> BuiltInMailboxIds = new[]
        {
            (Mailboxes.DefaultMailboxId, typeof(UnboundedMailbox)),
            ("akka.actor.mailbox.unbounded-queue-based", typeof(UnboundedMailbox)),
            ("akka.actor.mailbox.bounded-queue-based", typeof(BoundedMailbox)),
            ("akka.actor.mailbox.unbounded-deque-based", typeof(UnboundedDequeBasedMailbox)),
            ("akka.actor.mailbox.bounded-deque-based", typeof(BoundedDequeBasedMailbox)),
            ("akka.actor.mailbox.logger-queue", typeof(LoggerMailboxType)),

            // "unbounded" and "bounded" are not config ids at all - they are the two shortcut arms
            // LookupConfigurator answers by constructing a mailbox directly, which this change does not
            // touch. They are here so a later refactor of those arms cannot quietly break the switch-off
            // path with them.
            ("unbounded", typeof(UnboundedMailbox)),
            ("bounded", typeof(BoundedMailbox))
        };

        [Fact(DisplayName = "Mailboxes should resolve every mailbox id core ships when dynamic type loading is off")]
        public async Task Should_resolve_every_built_in_mailbox_id_When_dynamic_type_loading_is_disabled()
        {
            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, async () =>
            {
                var system = ActorSystem.Create("built-in-mailboxes-off");
                try
                {
                    foreach (var (id, expected) in BuiltInMailboxIds)
                    {
                        var mailboxType = system.Mailboxes.Lookup(id);
                        _output.WriteLine($"{id} -> {mailboxType.GetType().Name}");
                        mailboxType.Should().BeOfType(expected, $"[{id}] must resolve from the built-in table");
                    }
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }

        /// <summary>
        /// Akka.Hosting writes the full versioned assembly-qualified name into HOCON. The table holds only the
        /// bare and <c>"Ns.T, Akka"</c> spellings, so this is the case that pins the
        /// <c>TypeExtensions.StripAssemblyIdentity</c> normalization on the lookup. The second row names a
        /// version this build will never have, which is the whole reason the table has no versioned key.
        /// </summary>
        [Fact(DisplayName = "Mailboxes should resolve a built-in mailbox-type written as a full assembly-qualified name when dynamic type loading is off")]
        public async Task Should_resolve_an_assembly_qualified_mailbox_type_When_dynamic_type_loading_is_disabled()
        {
            var config = ConfigurationFactory.ParseString($@"
                aqn-mailbox {{
                    mailbox-type = ""{typeof(UnboundedDequeBasedMailbox).AssemblyQualifiedName}""
                }}
                foreign-version-mailbox {{
                    mailbox-type = ""Akka.Dispatch.BoundedDequeBasedMailbox, Akka, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null""
                }}");

            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, async () =>
            {
                var system = ActorSystem.Create("aqn-mailbox-off", config);
                try
                {
                    system.Mailboxes.Lookup("aqn-mailbox").Should().BeOfType<UnboundedDequeBasedMailbox>();
                    system.Mailboxes.Lookup("foreign-version-mailbox").Should().BeOfType<BoundedDequeBasedMailbox>();
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }

        /// <summary>
        /// Pins the widened matching rule the single-key table relies on: no space after the comma and a
        /// lower-cased assembly name are both spellings <see cref="Type.GetType(string)"/> itself accepted,
        /// and now <c>TypeExtensions.ToBuiltInAkkaTypeName</c> accepts them too.
        /// </summary>
        [Fact(DisplayName = "Mailboxes should resolve a built-in mailbox-type spelled without a comma space and with a lower-cased assembly name when dynamic type loading is off")]
        public async Task Should_resolve_a_loosely_spelled_mailbox_type_When_dynamic_type_loading_is_disabled()
        {
            var config = ConfigurationFactory.ParseString(@"
                loose-mailbox {
                    mailbox-type = ""Akka.Dispatch.BoundedDequeBasedMailbox,akka""
                }");

            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, async () =>
            {
                var system = ActorSystem.Create("loose-mailbox-off", config);
                try
                {
                    system.Mailboxes.Lookup("loose-mailbox").Should().BeOfType<BoundedDequeBasedMailbox>();
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }

        /// <summary>
        /// The explicit switch-ON regression guard for <c>mailbox-type</c>: the reflection fallback must still
        /// resolve a <see cref="MailboxType"/> that <c>BuiltInMailboxTypes</c> knows nothing about.
        /// </summary>
        [Fact(DisplayName = "Mailboxes should resolve a mailbox-type named in HOCON when dynamic type loading is on")]
        public async Task Should_resolve_a_custom_mailbox_type_When_dynamic_type_loading_is_enabled()
        {
            await AkkaFeaturesSpec.WithDynamicTypeLoading(true, async () =>
            {
                var system = ActorSystem.Create(
                    "custom-mailbox-on",
                    ConfigurationFactory.ParseString(CustomMailboxConfig));
                try
                {
                    system.Mailboxes.Lookup("custom-mailbox").Should().BeOfType<TestPriorityMailbox>();
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }

        [Fact(DisplayName = "Mailboxes should reject a mailbox-type that is not built in when dynamic type loading is off")]
        public async Task Should_throw_ConfigurationException_When_the_mailbox_type_is_not_built_in_and_dynamic_type_loading_is_disabled()
        {
            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, async () =>
            {
                var system = ActorSystem.Create(
                    "custom-mailbox-off",
                    ConfigurationFactory.ParseString(CustomMailboxConfig));
                try
                {
                    var exception = Assert.Throws<ConfigurationException>(
                        () => system.Mailboxes.Lookup("custom-mailbox"));

                    _output.WriteLine(exception.Message);
                    exception.Message.Should().Contain("custom-mailbox.mailbox-type");
                    exception.Message.Should().Contain(CustomMailboxTypeName);
                    exception.Message.Should().Contain(AkkaFeaturesSpec.SwitchName);
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }

        [Fact(DisplayName = "Mailboxes should reject an akka.actor.mailbox.requirements key that is not built in when dynamic type loading is off")]
        public async Task Should_throw_ConfigurationException_When_a_requirement_key_is_not_built_in_and_dynamic_type_loading_is_disabled()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.actor.mailbox.requirements {
                    ""Some.Unknown.Interface"" = akka.actor.mailbox.unbounded-queue-based
                }");

            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, () =>
            {
                // Mailboxes is constructed during ActorSystem.Create, so the throw surfaces from there
                var exception = Assert.Throws<ConfigurationException>(
                    () => ActorSystem.Create("unknown-requirement-off", config));

                _output.WriteLine(exception.Message);
                exception.Message.Should().Contain("akka.actor.mailbox.requirements");
                exception.Message.Should().Contain("Some.Unknown.Interface");
                exception.Message.Should().Contain(AkkaFeaturesSpec.SwitchName);
                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// The switch-ON contract for <c>akka.actor.mailbox.requirements</c>: a key that does not resolve is
        /// warned about and skipped, and the system still boots. The key here is a built-in interface name
        /// with a trailing space, which is the case that pins the requirement keys as NOT trimmed - trimming
        /// them would map this key onto the same <see cref="Type"/> as core's own unpadded key and make the
        /// binding dictionary throw <see cref="ArgumentException"/> out of <see cref="ActorSystem.Create"/>.
        /// </summary>
        [Fact(DisplayName = "Mailboxes should warn and skip an akka.actor.mailbox.requirements key that does not resolve when dynamic type loading is on")]
        public async Task Should_warn_and_skip_an_unresolvable_requirement_key_When_dynamic_type_loading_is_enabled()
        {
            const string paddedBuiltInKey = "Akka.Dispatch.IUnboundedMessageQueueSemantics ";
            var config = ConfigurationFactory.ParseString($@"
                akka.actor.mailbox.requirements {{
                    ""{paddedBuiltInKey}"" = akka.actor.mailbox.unbounded-queue-based
                }}");

            await AkkaFeaturesSpec.WithDynamicTypeLoading(true, async () =>
            {
                // Mailboxes warns while ActorSystem.Create is still running, so no EventStream subscriber can
                // be in place yet; a log filter is the only way to observe it.
                var warnings = new RecordingLogFilter();
                var system = ActorSystem.Create(
                    "padded-requirement-on",
                    ActorSystemSetup.Create(
                        BootstrapSetup.Create().WithConfig(config),
                        new LogFilterSetup([warnings])));
                try
                {
                    foreach (var warning in warnings.Messages)
                        _output.WriteLine(warning);

                    warnings.Messages.Should().Contain(m => m.Contains($"Mailbox Requirement mapping [{paddedBuiltInKey}]"));

                    // core's own unpadded binding is untouched
                    system.Mailboxes.LookupByQueueType(typeof(IUnboundedMessageQueueSemantics))
                        .Should().BeOfType<UnboundedMailbox>();
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }

        /// <summary>
        /// Records every WARNING the stdout logger is asked to print and keeps them all, so a spec can assert
        /// on warnings raised before the <see cref="EventStream"/> has any subscribers.
        /// </summary>
        private sealed class RecordingLogFilter : LogFilterBase
        {
            private readonly ConcurrentQueue<string> _messages = new();

            public IReadOnlyCollection<string> Messages => _messages;

            public override LogFilterType FilterType => LogFilterType.Content;

            public override LogFilterDecision ShouldKeepMessage(LogEvent content, string? expandedMessage = null)
            {
                if (content.LogLevel() == LogLevel.WarningLevel)
                    _messages.Enqueue(expandedMessage ?? content.Message?.ToString() ?? string.Empty);

                return LogFilterDecision.Keep;
            }
        }
    }
}
