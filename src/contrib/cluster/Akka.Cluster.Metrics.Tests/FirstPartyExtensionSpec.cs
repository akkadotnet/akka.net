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

namespace Akka.Cluster.Metrics.Tests
{
    public class FirstPartyExtensionSpec
    {
        [Fact(DisplayName = "The first-party extension table resolves the Cluster.Metrics provider to the type reflection does")]
        public void Should_resolve_same_type_as_reflection_When_name_is_first_party()
        {
            const string name = "Akka.Cluster.Metrics.ClusterMetricsExtensionProvider, Akka.Cluster.Metrics";

            ActorSystemImpl.TryCreateFirstPartyExtension(name)!.GetType()
                .Should().Be(Type.GetType(name, throwOnError: true));
        }
    }
}
