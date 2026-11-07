// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using Akka.Configuration;
using FluentAssertions;

namespace Akka.DistributedData.Tests.MultiNode;

public sealed class ReplicatorV2MixedBindingSpecConfig : ReplicatorSpecConfig
{
    public ReplicatorV2MixedBindingSpecConfig()
    {
        NodeConfig([First, Third], [ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on")]);
        NodeConfig([Second], [ConfigurationFactory.ParseString("akka.actor.serialization-v2 = off")]);
    }
}

/// <summary>
/// Runs the unchanged Replicator convergence, partition and quorum scenarios with opposing
/// serializer bindings. The existing MNTR transport switch covers classic and Artery separately.
/// </summary>
public sealed class ReplicatorV2MixedBindingSpec : ReplicatorSpec
{
    public ReplicatorV2MixedBindingSpec() : this(new ReplicatorV2MixedBindingSpecConfig())
    {
    }

    private ReplicatorV2MixedBindingSpec(ReplicatorV2MixedBindingSpecConfig config) : base(config)
    {
        var expected = config.Myself == config.Second ? 12 : 52;
        Sys.Serialization.FindSerializerFor(new Get(new GCounterKey("binding-check"), ReadLocal.Instance))
            .Identifier.Should().Be(expected, "this participant must use its configured protocol writer");
    }
}
