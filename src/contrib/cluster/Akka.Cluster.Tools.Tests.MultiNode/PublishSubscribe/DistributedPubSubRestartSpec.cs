//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubRestartSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Cluster.Tools.PublishSubscribe.Internal;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;

namespace Akka.Cluster.Tools.Tests.MultiNode.PublishSubscribe;

public class DistributedPubSubRestartSpecConfig : MultiNodeConfig
{
    public RoleName First { get; }
    public RoleName Second { get; }
    public RoleName Third { get; }

    public DistributedPubSubRestartSpecConfig()
    {
        First = Role("first");
        Second = Role("second");
        Third = Role("third");

        CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.cluster.pub-sub.gossip-interval = 500ms
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.auto-down-unreachable-after = off

                # De-flake: while third's dying system is still unbinding its port, first's
                # associate attempt can connect at the TCP level but never complete the Akka
                # protocol handshake. With dot-netty enabled, AkkaProtocolSettings.HandshakeTimeout
                # is read from connection-timeout (default 15s), so one such poisoned attempt plus
                # the default 5s retry gate burned ~20s of the identify window on loaded CI agents.
                # Fail fast and retry fast so multiple association cycles fit into the window.
                akka.remote.dot-netty.tcp.connection-timeout = 5s
                akka.remote.retry-gate-closed-for = 1s # fast restart

                # second waits at the ""end"" barrier for the full identify window on first,
                # which can exceed the default 30s barrier timeout
                akka.testconductor.barrier-timeout = 60s
            ").WithFallback(DistributedPubSub.DefaultConfig());

        TestTransport = true;
    }

    internal class Shutdown : ReceiveActor
    {
        public Shutdown()
        {
            Context.GetLogger().Info("Shutdown actor started on {0}", Context.System.Name);
            Receive<string>(str => str.Equals("shutdown"), _ =>
            {
                Context.System.Terminate();
            });
        }
    }
}

public class DistributedPubSubRestartSpec : MultiNodeClusterSpec
{
    private readonly DistributedPubSubRestartSpecConfig _config;

    public DistributedPubSubRestartSpec() : this(new DistributedPubSubRestartSpecConfig())
    {
    }

    protected DistributedPubSubRestartSpec(DistributedPubSubRestartSpecConfig config) : base(config, typeof(DistributedPubSubRestartSpec))
    {
        _config = config;
    }

    [MultiNodeFact]
    public async Task DistributedPubSubRestartSpecs()
    {
        await A_Cluster_with_DistributedPubSub_must_startup_3_node_cluster();
        await A_Cluster_with_DistributedPubSub_must_handle_restart_of_nodes_with_same_address();
    }

    public async Task A_Cluster_with_DistributedPubSub_must_startup_3_node_cluster()
    {
        await WithinAsync(15.Seconds(), async () =>
        {
            await JoinAsync(_config.First, _config.First);
            await JoinAsync(_config.Second, _config.First);
            await JoinAsync(_config.Third, _config.First);
            await EnterBarrierAsync("after-1");
        });
    }

    public async Task A_Cluster_with_DistributedPubSub_must_handle_restart_of_nodes_with_same_address()
    {
        // Sized to hold the pre-shutdown steps (~10s worst case), the 45s identify window
        // on first (which WithinAsync clamps to whatever remains here), and the post-barrier
        // delta checks. The JVM spec's 30s proved too tight on loaded Windows agents once a
        // single poisoned association cycle was in play. Also has to cover third's worst case
        // (~13s restart prelude plus its 45s receive window), which must out-last first's 45s
        // identify/deliver window - see the WhenTerminated comment below.
        await WithinAsync(90.Seconds(), async () =>
        {
            Mediator.Tell(new Subscribe("topic1", TestActor));
            await ExpectMsgAsync<SubscribeAck>();
            await CountAsync(3);

            await RunOnAsync(() =>
            {
                Mediator.Tell(new Publish("topic1", "msg1"));
                return Task.CompletedTask;
            }, _config.First);
            await EnterBarrierAsync("pub-msg1");

            await ExpectMsgAsync("msg1");
            await EnterBarrierAsync("got-msg1");

            // All nodes capture baseline DeltaCount before node-specific logic
            Mediator.Tell(DeltaCount.Instance);
            var oldDeltaCount = await ExpectMsgAsync<long>();
            await EnterBarrierAsync("old-delta-count");

            await RunOnAsync(async () =>
            {
                await EnterBarrierAsync("end");

                // Use a probe to isolate DeltaCount query from any stray messages in TestActor mailbox
                var probe = CreateTestProbe();
                Mediator.Tell(DeltaCount.Instance, probe.Ref);
                var deltaCount = await probe.ExpectMsgAsync<long>(5.Seconds());
                deltaCount.Should().Be(oldDeltaCount);
            }, _config.Second);

            await RunOnAsync(async () =>
            {
                var thirdAddress = (await NodeAsync(_config.Third)).Address;
                await TestConductor.Shutdown(_config.Third).WaitAsync(30.Seconds());

                // Must outlast third's full serial restart (old system termination, port rebind,
                // self-join, SubscribeAck, 5s gossip-isolation check) plus at least two failed
                // association cycles (connection-timeout + retry gate) against the dying endpoint.
                await WithinAsync(45.Seconds(), async () =>
                {
                    await AwaitAssertAsync(async () =>
                    {
                        // JVM parity (DistributedPubSubRestartSpec.scala): a FRESH probe per attempt.
                        // Reusing one probe across attempts livelocks: association establishment
                        // flushes the Identify messages buffered by the EndpointWriter as one burst
                        // of ActorIdentity(null) replies, and from then on every attempt sends one
                        // new Identify but reads one STALE null reply - the backlog never drains,
                        // so the loop fails fast forever even after /user/shutdown exists.
                        var identifyProbe = CreateTestProbe();
                        Sys.ActorSelection(new RootActorPath(thirdAddress) / "user" / "shutdown").Tell(new Identify(null), identifyProbe.Ref);
                        (await identifyProbe.ExpectMsgAsync<ActorIdentity>(2.Seconds())).Subject.Should().NotBeNull();
                    });
                });

                // At-most-once send across an association that is being re-established -
                // resend covers a lost first delivery.
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    if (attempt > 0)
                        await Task.Delay(TimeSpan.FromSeconds(2));

                    Sys.ActorSelection(new RootActorPath(thirdAddress) / "user" / "shutdown").Tell("shutdown");
                }

                await EnterBarrierAsync("end");

                // Use a probe to isolate DeltaCount query from stray ActorIdentity messages
                // that may still be arriving from AwaitAssertAsync Identify retries
                var deltaProbe = CreateTestProbe();
                Mediator.Tell(DeltaCount.Instance, deltaProbe.Ref);
                var deltaCount = await deltaProbe.ExpectMsgAsync<long>(5.Seconds());
                deltaCount.Should().Be(oldDeltaCount);
            }, _config.First);

            await RunOnAsync(async () =>
            {
                var node3Address = Cluster.Get(Sys).SelfAddress;
                await Sys.WhenTerminated.WaitAsync(30.Seconds());
                var newSystem = ActorSystem.Create(
                    Sys.Name,
                    ConfigurationFactory
                        .ParseString($"akka.remote.dot-netty.tcp.port={node3Address.Port}")
                        .WithFallback(Sys.Settings.Config));

                try
                {
                    // don't join the old cluster
                    await Cluster.Get(newSystem).JoinAsync(Cluster.Get(newSystem).SelfAddress);
                    var newMediator = DistributedPubSub.Get(newSystem).Mediator;
                    var probe = CreateTestProbe(newSystem);

                    newMediator.Tell(new Subscribe("topic2", probe.Ref), probe.Ref);
                    await probe.ExpectMsgAsync<SubscribeAck>();

                    // let them gossip, but Delta should not be exchanged
                    await probe.ExpectNoMsgAsync(5.Seconds());
                    newMediator.Tell(DeltaCount.Instance, probe.Ref);
                    await probe.ExpectMsgAsync(0L);

                    // Create shutdown actor AFTER verifying gossip isolation.
                    // First node will find this actor and send "shutdown" to terminate newSystem.
                    // We must complete the DeltaCount check above before this, otherwise there's
                    // a race where First triggers shutdown while we're still verifying.
                    newSystem.Log.Info("Creating shutdown actor on {0}", node3Address);
                    newSystem.ActorOf<DistributedPubSubRestartSpecConfig.Shutdown>("shutdown");

                    // Invariant: third's receive deadline must be >= first's 45s identify/deliver
                    // deadline. #8400 widened the sender (first) without widening the receiver
                    // (third), inverting this and producing the build-129150 failure: third gave
                    // up at exactly its old 30s mark while first's delivery landed ~2s later.
                    await newSystem.WhenTerminated.WaitAsync(45.Seconds());
                }
                finally
                {
                    await newSystem.Terminate().WaitAsync(45.Seconds());
                }
            }, _config.Third);
        });
    }

    protected override int InitialParticipantsValueFactory => Roles.Count;

    /// <summary>
    /// TestKitBase.Shutdown force-kills the ActorSystem after 5s by default ("Failed to stop
    /// [...] within [00:00:05]" in CI logs). On first, teardown races the draining of the
    /// gated / half-open association left over from third's restart, so give remoting time
    /// to flush and close cleanly instead of hard-stopping the guardian.
    /// </summary>
    protected override void Shutdown(ActorSystem system, TimeSpan? duration = null, bool verifySystemShutdown = false)
        => base.Shutdown(system, duration ?? TimeSpan.FromSeconds(30), verifySystemShutdown);

    private IActorRef CreateMediator()
    {
        return DistributedPubSub.Get(Sys).Mediator;
    }

    private IActorRef Mediator
    {
        get
        {
            return DistributedPubSub.Get(Sys).Mediator;
        }
    }

    private async Task JoinAsync(RoleName from, RoleName to)
    {
        await RunOnAsync(() =>
        {
            Cluster.Get(Sys).Join(Node(to).Address);
            CreateMediator();
            return Task.CompletedTask;
        }, from);
        await EnterBarrierAsync(from.Name + "-joined");
    }

    private async Task CountAsync(int expected)
    {
        var probe = CreateTestProbe();
        await AwaitAssertAsync(async () =>
        {
            Mediator.Tell(Count.Instance, probe.Ref);
            (await probe.ExpectMsgAsync<int>()).Should().Be(expected);
        });
    }
}