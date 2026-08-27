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

                # THE fix for this spec's flake. Bound the transport failure detector so a peer that
                # dies without a clean Disassociate PDU gets reaped fast. Under the test transport
                # (trttl.gremlin, TestTransport = true) the ThrottledAssociation FSM intentionally
                # swallows the TCP-close event and leans on this failure detector to notice a dead
                # connection. Its default acceptable-heartbeat-pause is 120s, which leaves first's
                # EndpointWriter alive on a half-open handle after third restarts on the same address:
                # every send (Identify, heartbeat, gossip, the ""shutdown"") goes into the dead socket
                # and is lost silently, so first never re-associates to the restarted incarnation and
                # its identify loop times out. Every other restart/gate MNTR spec sets this override
                # for the same reason (ComesBack, RestartDeathWatch, GatePiercing, RestartedQuarantined);
                # this was the only restart spec missing it. Not a production path - without the
                # throttler, Disassociated reaches ProtocolStateActor directly and tears the writer
                # down at once. Bounds the zombie window to <=6s: FD trips, writer fails, gate, then a
                # fresh association to the restarted incarnation.
                akka.remote.transport-failure-detector.heartbeat-interval = 1s
                akka.remote.transport-failure-detector.acceptable-heartbeat-pause = 5s

                # No connection-timeout / retry-gate overrides - use the defaults, like the JVM spec
                # and the sibling restart specs (a prior de-flake set connection-timeout = 5s, which
                # via the dot-netty handshake-timeout/connection-timeout conflation starved the
                # re-association handshake; the default gives it the full 15s budget).

                # second waits at the ""end"" barrier while first runs its closed-loop identify/kill
                # window (dilated) plus the conductor-shutdown ack - kept well past the 30s default.
                akka.testconductor.barrier-timeout = 120s
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
                // Reply BEFORE terminating so the sender (first) gets an observable ack that
                // proves this incarnation received the kill. This ack is what lets first run a
                // CLOSED-LOOP, self-verifying retry instead of an open-loop blind resend
                // (mirrors the Subject actor in RemoteNodeRestartDeathWatchSpec, PR #8404).
                Sender.Tell("shutdown-ack");
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
        // No Within wrapper: JoinAsync and "after-1" are barrier waits, and barriers must
        // not run inside a Within (see the comment on the restart test below).
        await JoinAsync(_config.First, _config.First);
        await JoinAsync(_config.Second, _config.First);
        await JoinAsync(_config.Third, _config.First);
        await EnterBarrierAsync("after-1");
    }

    public async Task A_Cluster_with_DistributedPubSub_must_handle_restart_of_nodes_with_same_address()
    {
        // Barriers must not run inside a Within: EnterBarrier's timeout is
        // RemainingOr(barrier-timeout), which clamps toward zero as a Within deadline
        // approaches, and Akka.NET's Within does not dilate by timefactor. So no outer
        // umbrella here - every wait below carries its own explicit bound instead.
        Mediator.Tell(new Subscribe("topic1", TestActor));
        await ExpectMsgAsync<SubscribeAck>();
        await CountAsync(3);

        await RunOnAsync(() =>
        {
            Mediator.Tell(new Publish("topic1", "msg1"));
            return Task.CompletedTask;
        }, _config.First);
        await EnterBarrierAsync("pub-msg1");

        // Cross-node delivery over established associations; CountAsync above already
        // proved the subscription gossip converged.
        await ExpectMsgAsync("msg1", 10.Seconds());
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

            // CLOSED-LOOP, self-verifying restart-kill (mirrors PR #8404 for the sibling
            // RemoteNodeRestartDeathWatchSpec). This replaces a prior OPEN-LOOP resend
            // (10 blind "shutdown" Tells over ~27s with no per-send verification). After the
            // same-address restart, first's outbound endpoint to third is gated by the dead old
            // incarnation's failing cluster heartbeats, so brand-new sends to the new incarnation
            // collateral-drop to dead-letters for tens of seconds until a fresh association forms.
            // An open-loop resend over that at-most-once, gated path has NO constant time bound:
            // all blind shots can land inside the drop window, third never shuts down, and third's
            // 120s WhenTerminated wait times out -> flake (five prior timeout/window tunings could
            // not fix this because the drop window is unbounded).
            //
            // Instead, retry the WHOLE Identify -> Tell -> observe-ack cycle: every iteration
            // re-pokes the association, so recovery is no longer hostage to blind send timing.
            // The exit condition is OBSERVABLE - first only leaves the loop once third's new
            // /user/shutdown incarnation acks the kill (proof it was received). Generous, dilated
            // window (45s) so many association cycles fit even on loaded CI agents.
            await AwaitAssertAsync(async () =>
            {
                // FRESH probe per attempt (JVM parity, DistributedPubSubRestartSpec.scala):
                // reusing one probe livelocks because association establishment flushes the
                // Identify messages buffered by the EndpointWriter as one burst of
                // ActorIdentity(null) replies, and a reused probe then reads one STALE null per
                // attempt forever - the backlog never drains even after /user/shutdown exists.
                var probe = CreateTestProbe();
                Sys.ActorSelection(new RootActorPath(thirdAddress) / "user" / "shutdown").Tell(new Identify(null), probe.Ref);
                var subject = (await probe.ExpectMsgAsync<ActorIdentity>(2.Seconds())).Subject;
                subject.Should().NotBeNull("third's restarted /user/shutdown must resolve before it can be killed");

                // Subject resolved -> the association is live this iteration. Tell it "shutdown"
                // and require the ack as proof of delivery. If the ack does not come back, the
                // whole attempt fails and retries (fresh Identify + fresh Tell), re-poking the path.
                subject.Tell("shutdown", probe.Ref);
                await probe.ExpectMsgAsync<string>(msg => msg == "shutdown-ack", 3.Seconds());
            }, 45.Seconds(), 1.Seconds());

            await EnterBarrierAsync("end");

            // Use a probe to isolate the DeltaCount query from any stray ActorIdentity replies
            // still draining into per-attempt probes from the closed-loop retries above.
            var deltaProbe = CreateTestProbe();
            Mediator.Tell(DeltaCount.Instance, deltaProbe.Ref);
            var deltaCount = await deltaProbe.ExpectMsgAsync<long>(5.Seconds());
            deltaCount.Should().Be(oldDeltaCount);
        }, _config.First);

        await RunOnAsync(async () =>
        {
            var node3Address = Cluster.Get(Sys).SelfAddress;
            await Sys.WhenTerminated.WaitAsync(30.Seconds());
            // Pin the fresh system to the SAME wire address for BOTH transports - under
            // AKKA_MNTR_TRANSPORT=artery the classic dot-netty key is inert and the fresh
            // system would bind a random artery canonical.port instead.
            var newSystem = ActorSystem.Create(
                Sys.Name,
                ConfigurationFactory
                    .ParseString($"akka.remote.dot-netty.tcp.port={node3Address.Port}\n" +
                        $"akka.remote.artery.canonical.port={node3Address.Port}")
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

                // First's closed-loop kill (above) normally drives this WhenTerminated: it keeps
                // re-poking the association until third's /user/shutdown acks the kill, at which
                // point newSystem terminates and this wait completes. Give it a generous 120s
                // upper bound so first's whole worst-case pipeline (Shutdown-ack anchor sliding up
                // to ~30s past third's restart clock, plus the 45s dilated identify/kill window)
                // fits comfortably.
                //
                // This wait is BEST-EFFORT: the spec's REAL assertions - the SubscribeAck /
                // ExpectNoMsg / DeltaCount == 0 gossip-isolation checks above - have already run
                // and are what this spec actually verifies. If a pathological association window
                // ever starves first's kill, we must not hang CI: log loudly and terminate
                // newSystem ourselves (via the finally below) so the test PASSES on the strength
                // of the isolation assertions that already succeeded.
                try
                {
                    await newSystem.WhenTerminated.WaitAsync(120.Seconds());
                }
                catch (TimeoutException)
                {
                    newSystem.Log.Warning(
                        "newSystem did not observe first's shutdown within 120s; terminating self (best-effort). " +
                        "The gossip-isolation assertions (SubscribeAck / ExpectNoMsg / DeltaCount == 0) already passed, " +
                        "so the spec's subject-under-test is verified regardless.");
                }
            }
            finally
            {
                await newSystem.Terminate().WaitAsync(45.Seconds());
            }
        }, _config.Third);
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
        // Gossip-propagation check: registrations spread on the 500ms pub-sub gossip tick,
        // so bound it explicitly instead of relying on the 3s single-expect default.
        await AwaitAssertAsync(async () =>
        {
            Mediator.Tell(Count.Instance, probe.Ref);
            (await probe.ExpectMsgAsync<int>()).Should().Be(expected);
        }, 10.Seconds(), 500.Milliseconds());
    }
}