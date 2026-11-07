// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tests.Serialization;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode;

public sealed class ClusterSerializationV2InteropConfig : MultiNodeConfig
{
    public RoleName Legacy { get; }
    public RoleName V2 { get; }
    public RoleName Pinned { get; }

    public ClusterSerializationV2InteropConfig()
    {
        Legacy = Role("legacy");
        V2 = Role("v2");
        Pinned = Role("pinned");
        CommonConfig = ConfigurationFactory.ParseString("""
            akka.loglevel = INFO
            akka.coordinated-shutdown.terminate-actor-system = off
            """).WithFallback(MultiNodeClusterSpec.ClusterConfigWithFailureDetectorPuppet());
        NodeConfig(new[] { Legacy }, new[] { Profile("legacy", "1.2.1", false) });
        NodeConfig(new[] { V2 }, new[] { Profile("v2", "1.2.2", true) });
        NodeConfig(new[] { Pinned }, new[] { ConfigurationFactory.ParseString("""
            akka.actor.serialization-bindings {
              "Akka.Cluster.IClusterMessage, Akka.Cluster" = akka-cluster
              "Akka.Cluster.Routing.ClusterRouterPool, Akka.Cluster" = akka-cluster
            }
            """).WithFallback(Profile("pinned", "1.2.3", true)) });
    }

    private static Config Profile(string role, string version, bool v2) => ConfigurationFactory.ParseString($$"""
        akka.actor.serialization-v2 = {{(v2 ? "on" : "off")}}
        akka.cluster.roles = ["backend", "{{role}}"]
        akka.cluster.app-version = "{{version}}"
        """);
}

/// <summary>
/// Real membership/gossip/leave across three processes with off, on, and explicit legacy pins.
/// MultiNodeSpec captures per-node logs; transport selection also supports AKKA_MNTR_TRANSPORT=artery.
/// </summary>
public sealed class ClusterSerializationV2InteropSpec : MultiNodeClusterSpec
{
    private readonly ClusterSerializationV2InteropConfig _config;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public ClusterSerializationV2InteropSpec() : this(new ClusterSerializationV2InteropConfig())
    {
    }

    private ClusterSerializationV2InteropSpec(ClusterSerializationV2InteropConfig config)
        : base(config, typeof(ClusterSerializationV2InteropSpec))
    {
        _config = config;
    }

    [MultiNodeFact(DisplayName = "Should_ConvergeMembershipAndRemoveAV2Member_When_ClusterWritersDiffer")]
    public async Task Should_ConvergeMembershipAndRemoveAV2Member_When_ClusterWritersDiffer()
    {
        var writerId = Myself.Equals(_config.V2) ? 45 : 5;
        foreach (var item in ClusterWireCases.Create())
            Sys.Serialization.FindSerializerFor(item.Message).Identifier.Should().Be(writerId, item.Name);
        Log.Info("Cluster interop: process [{0}], role [{1}], writer [{2}]", Environment.ProcessId, Myself.Name, writerId);

        Sys.ActorOf(Props.Create(() => new ProcessIdentity()), "process-identity");
        await EnterBarrierAsync("identity-ready");
        foreach (var other in Roles.Where(role => !role.Equals(Myself)))
        {
            var remote = await Sys.ActorSelection(await NodeAsync(other) / "user" / "process-identity").ResolveOne(Timeout);
            remote.Should().BeOfType<RemoteActorRef>();
            remote.Tell(Environment.ProcessId, TestActor);
            (await ExpectMsgAsync<int>(Timeout)).Should().NotBe(Environment.ProcessId);
        }
        await EnterBarrierAsync("processes-distinct");

        RunOn(() => Cluster.Join(Cluster.SelfAddress), _config.Legacy);
        await RunOnAsync(() => AwaitMembersUpAsync(1), _config.Legacy);
        await EnterBarrierAsync("seed-up");
        RunOn(() => Cluster.JoinSeedNodes(ImmutableList.Create(GetAddress(_config.Legacy))), _config.V2, _config.Pinned);
        await AwaitMembersUpAsync(3);
        await AwaitAssertAsync(() =>
        {
            AssertMember(_config.Legacy, "legacy", "1.2.1");
            AssertMember(_config.V2, "v2", "1.2.2");
            AssertMember(_config.Pinned, "pinned", "1.2.3");
            ClusterView.SeenBy.Should().Contain(GetAddress(_config.Legacy))
                .And.Contain(GetAddress(_config.V2)).And.Contain(GetAddress(_config.Pinned));
        }, TimeSpan.FromSeconds(30));
        await EnterBarrierAsync("membership-converged");

        RunOn(() => Cluster.Leave(Cluster.SelfAddress), _config.V2);
        await RunOnAsync(async () =>
        {
            await AwaitAssertAsync(() =>
            {
                ClusterView.Members.Count.Should().Be(2);
                ClusterView.Members.Select(member => member.Address).Should().NotContain(GetAddress(_config.V2));
                AssertMember(_config.Legacy, "legacy", "1.2.1");
                AssertMember(_config.Pinned, "pinned", "1.2.3");
            }, TimeSpan.FromSeconds(30));
        }, _config.Legacy, _config.Pinned);
        await EnterBarrierAsync("v2-member-removed");
    }

    private void AssertMember(RoleName role, string memberRole, string version)
    {
        var member = ClusterView.Members.Single(member => member.Address.Equals(GetAddress(role)));
        member.Status.Should().Be(MemberStatus.Up);
        member.UpNumber.Should().BeGreaterThan(0);
        member.Roles.Should().BeEquivalentTo(ImmutableHashSet.Create("backend", memberRole));
        member.AppVersion.Version.Should().Be(version);
    }

    private sealed class ProcessIdentity : ReceiveActor
    {
        public ProcessIdentity()
        {
            Receive<int>(_ => Sender.Tell(Environment.ProcessId, Self));
        }
    }
}
