﻿// -----------------------------------------------------------------------
//  <copyright file="JoinSeedNodeSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Threading;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;

namespace Akka.Cluster.Tests.MultiNode;

public class JoinSeedNodeConfig : MultiNodeConfig
{
    public JoinSeedNodeConfig()
    {
        Seed1 = Role("seed1");
        Seed2 = Role("seed2");
        Seed3 = Role("seed3");
        Ordinary1 = Role("ordinary1");
        Ordinary2 = Role("ordinary2");

        CommonConfig = MultiNodeLoggingConfig.LoggingConfig.WithFallback(DebugConfig(true))
            .WithFallback(ConfigurationFactory.ParseString(@"akka.cluster.publish-stats-interval = 25s"))
            .WithFallback(MultiNodeClusterSpec.ClusterConfig());
    }

    public RoleName Seed1 { get; }

    public RoleName Seed2 { get; }

    public RoleName Seed3 { get; }

    public RoleName Ordinary1 { get; }

    public RoleName Ordinary2 { get; }
}

public class JoinSeedNodeSpec : MultiNodeClusterSpec
{
    private readonly JoinSeedNodeConfig _config;

    private ImmutableList<Address> _seedNodes;

    public JoinSeedNodeSpec() : this(new JoinSeedNodeConfig())
    {
    }

    protected JoinSeedNodeSpec(JoinSeedNodeConfig config) : base(config, typeof(JoinSeedNodeSpec))
    {
        _config = config;
    }

    [MultiNodeFact]
    public void JoinSeedNodeSpecs()
    {
        _seedNodes = ImmutableList.Create(GetAddress(_config.Seed1), GetAddress(_config.Seed2),
            GetAddress(_config.Seed3));
        A_cluster_with_seed_nodes_must_be_able_to_start_the_seed_nodes_concurrently();
        A_cluster_with_seed_nodes_must_be_able_to_join_the_seed_nodes();
    }

    public void A_cluster_with_seed_nodes_must_be_able_to_start_the_seed_nodes_concurrently()
    {
        RunOn(() =>
        {
            // test that first seed doesn't have to be started first
            Thread.Sleep(3000);
        }, _config.Seed1);

        RunOn(() =>
        {
            Cluster.JoinSeedNodes(_seedNodes);
            RunOn(() =>
            {
                //verify that we can call this multiple times with no issue                    
                Cluster.JoinSeedNodes(_seedNodes);
            }, _config.Seed3);
            AwaitMembersUp(3);
        }, _config.Seed1, _config.Seed2, _config.Seed3);

        EnterBarrier("after-1");
    }

    public void A_cluster_with_seed_nodes_must_be_able_to_join_the_seed_nodes()
    {
        RunOn(() => { Cluster.JoinSeedNodes(_seedNodes); }, _config.Ordinary1, _config.Ordinary2);

        AwaitMembersUp(Roles.Count);
        EnterBarrier("after-2");
    }
}