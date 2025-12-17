//-----------------------------------------------------------------------
// <copyright file="AttemptSysMsgRedeliverySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using FluentAssertions;
using FluentAssertions.Extensions;

namespace Akka.Cluster.Tests.MultiNode;

public class AttemptSysMsgRedeliverySpecConfig : MultiNodeConfig
{
    internal class Echo : ReceiveActor
    {
        public Echo()
        {
            ReceiveAny(m => Sender.Tell(m));
        }
    }

    public RoleName First { get; }
    public RoleName Second { get; }
    public RoleName Third { get; }

    public AttemptSysMsgRedeliverySpecConfig()
    {
        First = Role("first");
        Second = Role("second");
        Third = Role("third");

        CommonConfig = DebugConfig(false)
            .WithFallback(MultiNodeClusterSpec.ClusterConfig());

        TestTransport = true;
    }
}

public class AttemptSysMsgRedeliverySpec : MultiNodeClusterSpec
{
    private readonly AttemptSysMsgRedeliverySpecConfig _config;

    public AttemptSysMsgRedeliverySpec() : this(new AttemptSysMsgRedeliverySpecConfig())
    {
    }

    protected AttemptSysMsgRedeliverySpec(AttemptSysMsgRedeliverySpecConfig config) : base(config, typeof(AttemptSysMsgRedeliverySpec))
    {
        _config = config;
    }

    [MultiNodeFact]
    public async Task AttemptSysMsgRedeliverySpecs()
    {
        await AttemptSysMsgRedelivery_must_reach_initial_convergence();
        await AttemptSysMsgRedelivery_must_redeliver_system_message_after_inactivity();
    }

    private async Task AttemptSysMsgRedelivery_must_reach_initial_convergence()
    {
        await AwaitClusterUpAsync(CancellationToken.None, _config.First, _config.Second, _config.Third);
        await EnterBarrierAsync("after-1");
    }

    private async Task AttemptSysMsgRedelivery_must_redeliver_system_message_after_inactivity()
    {
        Sys.ActorOf(Props.Create<AttemptSysMsgRedeliverySpecConfig.Echo>(), "echo");
        await EnterBarrierAsync("echo-started");

        Sys.ActorSelection(await NodeAsync(_config.First) / "user" / "echo").Tell(new Identify(null));
        var firstRef = (await ExpectMsgAsync<ActorIdentity>()).Subject;
        Sys.ActorSelection(await NodeAsync(_config.First) / "user" / "echo").Tell(new Identify(null));
        var secondRef = (await ExpectMsgAsync<ActorIdentity>()).Subject;
        await EnterBarrierAsync("refs-retrieved");

        await RunOnAsync(async () =>
        {
            await TestConductor.BlackholeAsync(_config.First, _config.Second, ThrottleTransportAdapter.Direction.Both);
        }, _config.First);
        await EnterBarrierAsync("blackhole");

        RunOn(() =>
        {
            Watch(secondRef);
        }, _config.First, _config.Third);

        RunOn(() =>
        {
            Watch(firstRef);
        }, _config.Second);
        await EnterBarrierAsync("watch-established");

        await RunOnAsync(async () =>
        {
            await TestConductor.PassThroughAsync(_config.First, _config.Second, ThrottleTransportAdapter.Direction.Both);
        }, _config.First);
        await EnterBarrierAsync("pass-through");

        Sys.ActorSelection("/user/echo").Tell(PoisonPill.Instance);

        await RunOnAsync(async () =>
        {
            await ExpectTerminatedAsync(secondRef, 10.Seconds());
        }, _config.First, _config.Third);

        await RunOnAsync(async () =>
        {
            await ExpectTerminatedAsync(firstRef, 10.Seconds());
        }, _config.Second);

        await EnterBarrierAsync("done");
    }
}