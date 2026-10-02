//-----------------------------------------------------------------------
// <copyright file="FirstPartyExtensionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using Akka.Actor.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    public class FirstPartyExtensionSpec
    {
        // Sharding references both Akka.DistributedData and Akka.Cluster.Tools, so their providers load here
        [Theory(DisplayName = "The first-party extension table resolves an akka.extensions name to the type reflection does")]
        [InlineData("Akka.DistributedData.DistributedDataProvider, Akka.DistributedData")]
        [InlineData("Akka.DistributedData.DistributedDataProvider,Akka.DistributedData")]
        [InlineData("Akka.DistributedData.DistributedDataProvider, akka.distributeddata")]
        [InlineData("Akka.DistributedData.DistributedDataProvider, Akka.DistributedData, Version=1.0.0.0, Culture=neutral")]
        [InlineData("Akka.Cluster.Tools.PublishSubscribe.DistributedPubSubExtensionProvider, Akka.Cluster.Tools")]
        [InlineData("Akka.Cluster.Tools.Client.ClusterClientReceptionistExtensionProvider, Akka.Cluster.Tools")]
        public void Should_resolve_same_type_as_reflection_When_name_is_first_party(string name)
        {
            ActorSystemImpl.TryCreateFirstPartyExtension(name)
                .Should().BeOfType(Type.GetType(name, throwOnError: true));
        }
    }
}
