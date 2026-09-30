//-----------------------------------------------------------------------
// <copyright file="ExtensionsSetupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Actor.Setup
{
    // each test starts its own ActorSystem from an ExtensionsSetup; Sys is only the TestKit's
    public class ExtensionsSetupSpec : AkkaSpec
    {
        private const string HoconCountingFqn = "Akka.Tests.Actor.Setup.ExtensionsSetupSpec+HoconCountingExtension, Akka.Tests";
        private const string FailingFqn = "Akka.Tests.Actor.Setup.ExtensionsSetupSpec+FailingExtension, Akka.Tests";

        public ExtensionsSetupSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static ActorSystemSetup SetupWith(string hocon, params IExtensionId[] extensionIds)
            => BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(hocon))
                .And(ExtensionsSetup.Create(extensionIds));

        [Fact(DisplayName = "An extension in ExtensionsSetup is registered when the ActorSystem starts")]
        public async Task Should_register_extension_When_named_in_Setup()
        {
            var system = ActorSystem.Create("setup-only", SetupWith("", new OtherTestExtension()));
            try
            {
                system.HasExtension<OtherTestExtensionImpl>().Should().BeTrue();
                system.GetExtension<OtherTestExtensionImpl>().System.Should().Be(system);
            }
            finally
            {
                await ShutdownAsync(system);
            }
        }

        [Fact(DisplayName = "A failing extension in ExtensionsSetup fails startup the same way it does when named in akka.extensions")]
        public void Should_fail_like_HOCON_When_Setup_extension_throws()
        {
            // today's akka.extensions path only skips names it cannot resolve or construct; CreateExtension throwing fails startup
            var viaHocon = () => ActorSystem.Create("failing-hocon", SetupWith($"akka.extensions = [\"{FailingFqn}\"]"));
            var viaSetup = () => ActorSystem.Create("failing-setup", SetupWith("", new FailingExtension()));

            viaHocon.Should().Throw<FailingExtension.TestException>();
            viaSetup.Should().Throw<FailingExtension.TestException>();
        }

        [Fact(DisplayName = "An extension named in both akka.extensions and ExtensionsSetup is registered once, from the Setup's id")]
        public async Task Should_register_once_from_Setup_When_named_in_HOCON_and_Setup()
        {
            SetupCountingExtension.Created = 0;
            HoconCountingExtension.Created = 0;
            var system = ActorSystem.Create("hocon-and-setup",
                SetupWith($"akka.extensions = [\"{HoconCountingFqn}\"]", new SetupCountingExtension()));
            try
            {
                system.HasExtension<CountingExtensionImpl>().Should().BeTrue();
                SetupCountingExtension.Created.Should().Be(1);
                HoconCountingExtension.Created.Should().Be(0);
            }
            finally
            {
                await ShutdownAsync(system);
            }
        }

        [Fact(DisplayName = "ExtensionsSetup rejects a null extension id")]
        public void Should_throw_When_Setup_has_null_id()
        {
            var create = () => ExtensionsSetup.Create(new OtherTestExtension(), null!);
            create.Should().Throw<ArgumentException>();
        }

        [Theory(DisplayName = "The first-party extension table ignores names that are not Akka's own extensions")]
        [InlineData(HoconCountingFqn)]
        [InlineData("Akka.DistributedData.DistributedDataProvider")] // no assembly: Type.GetType from Akka.dll would not find it either
        [InlineData("Akka.DistributedData.DistributedDataProvider, Akka.Cluster.Tools")]
        [InlineData("Akka.DistributedData.DistributedDataProviderX, Akka.DistributedData")]
        public void Should_resolve_nothing_When_name_is_not_first_party(string name)
        {
            ActorSystemImpl.TryCreateFirstPartyExtension(name).Should().BeNull();
        }

        [Fact(DisplayName = "A first-party extension whose assembly is absent counts as absent")]
        public void Should_resolve_nothing_When_first_party_assembly_is_absent()
        {
            // Akka.Tests does not reference Akka.DistributedData; the contrib test projects cover the present case
            ActorSystemImpl.TryCreateFirstPartyExtension("Akka.DistributedData.DistributedDataProvider, Akka.DistributedData")
                .Should().BeNull();
        }

        public sealed class SetupCountingExtension : ExtensionIdProvider<CountingExtensionImpl>
        {
            public static int Created;

            public override CountingExtensionImpl CreateExtension(ExtendedActorSystem system)
            {
                Interlocked.Increment(ref Created);
                return new CountingExtensionImpl();
            }
        }

        public sealed class HoconCountingExtension : ExtensionIdProvider<CountingExtensionImpl>
        {
            public static int Created;

            public override CountingExtensionImpl CreateExtension(ExtendedActorSystem system)
            {
                Interlocked.Increment(ref Created);
                return new CountingExtensionImpl();
            }
        }

        public sealed class CountingExtensionImpl : IExtension
        {
        }

        public sealed class FailingExtension : ExtensionIdProvider<FailingExtensionImpl>
        {
            public override FailingExtensionImpl CreateExtension(ExtendedActorSystem system)
                => throw new TestException();

            public sealed class TestException : Exception
            {
            }
        }

        public sealed class FailingExtensionImpl : IExtension
        {
        }
    }
}
