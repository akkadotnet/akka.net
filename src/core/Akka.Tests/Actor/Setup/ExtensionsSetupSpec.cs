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
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Actor.Setup
{
    public class ExtensionsSetupSpec
    {
        private const string CountingFqn = "Akka.Tests.Actor.Setup.ExtensionsSetupSpec+CountingExtension, Akka.Tests";
        private const string FailingFqn = "Akka.Tests.Actor.Setup.ExtensionsSetupSpec+FailingExtension, Akka.Tests";

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
                await system.Terminate();
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

        [Fact(DisplayName = "An extension named in both akka.extensions and ExtensionsSetup is registered once")]
        public async Task Should_register_once_When_named_in_HOCON_and_Setup()
        {
            CountingExtension.Created = 0;
            var system = ActorSystem.Create("hocon-and-setup",
                SetupWith($"akka.extensions = [\"{CountingFqn}\"]", new CountingExtension()));
            try
            {
                system.HasExtension<CountingExtensionImpl>().Should().BeTrue();
                CountingExtension.Created.Should().Be(1);
            }
            finally
            {
                await system.Terminate();
            }
        }

        [Theory(DisplayName = "The first-party extension table ignores names that are not Akka's own extensions")]
        [InlineData(CountingFqn)]
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

        public sealed class CountingExtension : ExtensionIdProvider<CountingExtensionImpl>
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
