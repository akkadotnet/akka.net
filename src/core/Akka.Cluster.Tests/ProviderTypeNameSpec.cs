//-----------------------------------------------------------------------
// <copyright file="ProviderTypeNameSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using Akka.Actor;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tests
{
    /// <summary>
    /// <see cref="ProviderSelection.ClusterActorRefProvider"/> is a compile-time constant that
    /// <c>ActorSystemImpl.ConfigureProvider</c> hands to <see cref="Type.GetType(string)"/> so the trimmer can
    /// keep the type. A rename or a move of <see cref="ClusterActorRefProvider"/> would not break the build,
    /// only the runtime lookup - this spec turns that into a compile-and-test failure instead.
    /// </summary>
    public class ProviderTypeNameSpec
    {
        [Fact(DisplayName = "Should_resolve_ClusterActorRefProvider_When_loading_the_ProviderSelection_constant")]
        public void Should_resolve_ClusterActorRefProvider_When_loading_the_ProviderSelection_constant()
        {
            var providerType = Type.GetType(ProviderSelection.ClusterActorRefProvider);

            providerType.Should().NotBeNull(
                $"[{ProviderSelection.ClusterActorRefProvider}] must stay resolvable - ActorSystemImpl resolves the cluster provider from this constant");
            typeof(IActorRefProvider).IsAssignableFrom(providerType).Should().BeTrue();
            providerType.Should().Be(typeof(ClusterActorRefProvider));
        }
    }
}
