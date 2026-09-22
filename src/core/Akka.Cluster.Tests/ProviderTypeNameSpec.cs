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
    /// The <see cref="ProviderSelection"/> type-name constants are the only thing standing between
    /// <c>akka.actor.provider</c> and the provider type now that <c>ActorSystemImpl.CreateProvider</c> resolves
    /// them through an annotated string parameter. Renaming or moving a provider would not break the build --
    /// only the runtime lookup, and only in a trimmed or AOT-published app. This spec turns that into a test
    /// failure. It lives in Akka.Cluster.Tests because that project references Akka.Remote as well, so all
    /// three constants resolve here.
    /// </summary>
    public class ProviderTypeNameSpec
    {
        [Theory(DisplayName = "Should_resolve_an_IActorRefProvider_When_loading_a_ProviderSelection_type_name_constant")]
        [InlineData(ProviderSelection.LocalActorRefProvider)]
        [InlineData(ProviderSelection.RemoteActorRefProvider)]
        [InlineData(ProviderSelection.ClusterActorRefProvider)]
        public void Should_resolve_an_IActorRefProvider_When_loading_a_ProviderSelection_type_name_constant(string typeName)
        {
            var providerType = Type.GetType(typeName);

            providerType.Should().NotBeNull(
                $"[{typeName}] must stay resolvable - ActorSystemImpl resolves built-in providers from these constants");
            typeof(IActorRefProvider).IsAssignableFrom(providerType).Should().BeTrue(
                $"[{typeName}] must name an {nameof(IActorRefProvider)} implementation");
        }
    }
}
