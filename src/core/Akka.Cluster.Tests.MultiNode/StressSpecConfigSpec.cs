//-----------------------------------------------------------------------
// <copyright file="StressSpecConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Xunit;

namespace Akka.Cluster.Tests.MultiNode;

/// <summary>
/// Plain (non multi-node) unit tests for <see cref="StressSpecConfig"/>'s per-phase node-count
/// arithmetic -- in particular, that <see cref="StressSpecConfig.BuildConfig"/> automatically
/// shrinks the `nr-of-nodes-*` phase counts so a reduced `MNTR_STRESSSPEC_NODECOUNT` (used on the
/// 2-vCPU hosted CI agents) still produces a consistent <see cref="StressSpecConfig.Settings"/>
/// instead of throwing.
///
/// These tests do not spin up an actor system or a multi-node run --
/// <see cref="StressSpecConfig.Settings"/> only reads plain HOCON values.
///
/// Unlike dev's 10-node reference config (where every joining phase defaults to 1 node and only
/// the leaving/shutdown side needs to shrink), this branch's 13-node reference config defaults
/// every joining phase to 2 nodes as well, so reaching the same practical floor of 7 requires
/// <see cref="StressSpecConfig.BuildConfig"/> to shrink both sides: the joining phases halve from
/// 2 to 1 each, and the leaving/shutdown phases drop their "-large" one-by-one variants and halve
/// the simultaneous "leaving"/"shutdown" counts from 2 to 1 -- the same technique dev's config
/// uses, just applied to one more group of phases.
/// </summary>
public class StressSpecConfigSpec
{
    private static StressSpecConfig.Settings BuildSettings(int totalNumberOfNodes)
    {
        var config = ConfigurationFactory.ParseString(StressSpecConfig.BuildConfig(totalNumberOfNodes));
        return new StressSpecConfig.Settings(config, totalNumberOfNodes);
    }

    [Fact]
    public void Settings_at_the_13_node_reference_count_uses_the_full_defaults()
    {
        var settings = BuildSettings(13);

        Assert.Equal(2, settings.NumberOfNodesJoiningToSeedNodesInitially);
        Assert.Equal(2, settings.NumberOfNodesJoiningOneByOneSmall);
        Assert.Equal(2, settings.NumberOfNodesJoiningOneByOneLarge);
        Assert.Equal(2, settings.NumberOfNodesJoiningToOneNode);
        Assert.Equal(1, settings.NumberOfNodesLeavingOneByOneLarge);
        Assert.Equal(1, settings.NumberOfNodesShutdownOneByOneLarge);
        Assert.Equal(2, settings.NumberOfNodesLeaving);
        Assert.Equal(2, settings.NumberOfNodesShutdown);

        // 3 seed + 2+2+2+2 fixed joining = 11, so 2 of the 13 nodes are left to join to the seed
        // nodes as a batch
        Assert.Equal(2, settings.NumberOfNodesJoiningToSeedNodes);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void Settings_below_13_nodes_does_not_throw(int totalNumberOfNodes)
    {
        var settings = BuildSettings(totalNumberOfNodes);
        Assert.Equal(totalNumberOfNodes, settings.TotalNumberOfNodes);
    }

    [Fact]
    public void Settings_at_7_nodes_shrinks_every_phase_to_exactly_fit()
    {
        var settings = BuildSettings(7);

        // every joining phase that defaults to 2 halves to 1
        Assert.Equal(1, settings.NumberOfNodesJoiningToSeedNodesInitially);
        Assert.Equal(1, settings.NumberOfNodesJoiningOneByOneSmall);
        Assert.Equal(1, settings.NumberOfNodesJoiningOneByOneLarge);
        Assert.Equal(1, settings.NumberOfNodesJoiningToOneNode);

        // the two "-large" one-by-one phases are dropped, and the simultaneous "leaving"/
        // "shutdown" phases are halved from 2 to 1, freeing exactly the nodes a 7-node run needs
        Assert.Equal(0, settings.NumberOfNodesLeavingOneByOneLarge);
        Assert.Equal(0, settings.NumberOfNodesShutdownOneByOneLarge);
        Assert.Equal(1, settings.NumberOfNodesLeaving);
        Assert.Equal(1, settings.NumberOfNodesShutdown);

        // the joining phases exactly consume all 7 nodes (3 seed + 4 halved singleton joins), so
        // there are none left over to join to the seed nodes as a batch
        Assert.Equal(0, settings.NumberOfNodesJoiningToSeedNodes);

        // the leaving/shutdown phases must still leave the 3 master-hosting nodes alone
        var leavingAndShutdown = settings.NumberOfNodesLeavingOneByOneSmall +
                                  settings.NumberOfNodesLeavingOneByOneLarge +
                                  settings.NumberOfNodesLeaving +
                                  settings.NumberOfNodesShutdownOneByOneSmall +
                                  settings.NumberOfNodesShutdownOneByOneLarge +
                                  settings.NumberOfNodesShutdown;
        Assert.True(leavingAndShutdown <= settings.TotalNumberOfNodes - 3);
    }

    [Fact]
    public void Settings_below_7_nodes_still_throws_because_the_joining_phases_alone_need_7()
    {
        // 3 seed nodes + 4 halved singleton join phases (joining-to-seed-initially, one-by-one-
        // small, one-by-one-large, joining-to-one, each 1 node once shrunk) need >= 7 nodes
        // regardless of how far the leaving/shutdown phases are shrunk, so 7 is the practical
        // floor -- the same floor dev's 10-node config reaches, even though this branch starts
        // from a heavier 13-node/2-per-phase default.
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildSettings(6));
    }
}
