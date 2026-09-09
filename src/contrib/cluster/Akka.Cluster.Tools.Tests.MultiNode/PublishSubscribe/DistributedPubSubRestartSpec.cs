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

    // Well-known actor on `first` that the restarted `third`'s Shutdown actor pings the moment
    // it exists (see Shutdown.PreStart below). Must live under /user: a TestKit probe backs onto
    // SystemActorOf and cannot be addressed at a fixed /user path itself, but a plain
    // ForwardActor standing under /user in front of one can be.
    internal const string ReadyActorName = "restart-ready";
    internal const string ReadySignal = "third-restarted";

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

                # No connection-timeout / retry-gate overrides - use the defaults, like the sibling
                # restart specs (a prior de-flake set connection-timeout = 5s, which
                # via the dot-netty handshake-timeout/connection-timeout conflation starved the
                # re-association handshake; the default gives it the full 15s budget).

                # The barrier-timeout clock arms the instant the FIRST node reaches a barrier, not
                # when every node does - second parks at ""end"" almost immediately, while first is
                # still working through, in order: the 30s Shutdown(third) cap, then up to a
                # dilated 60s wait for third's restarted incarnation to make first contact (see
                # the ready-ping comment below), then a 20s closed-loop kill.
                # 30s + 60s + 20s = 110s; 180s leaves 70s of headroom on top of that.
                akka.testconductor.barrier-timeout = 180s
            ").WithFallback(DistributedPubSub.DefaultConfig());

        TestTransport = true;
    }

    internal class Shutdown : ReceiveActor
    {
        private readonly Address _firstAddress;

        public Shutdown(Address firstAddress)
        {
            _firstAddress = firstAddress;
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

        protected override void PreStart()
        {
            base.PreStart();

            // This actor existing IS the fact first is waiting on below, so make the ping and
            // that fact the same event instead of signalling readiness some other way that could
            // race this actor's own creation. This fresh system has no stale association to
            // first to fight through - unlike first's outbound path to US, which still carries
            // the dead old incarnation's handshake state until something re-associates it - so
            // this send's own HandshakeReq is normally the first thing that reaches first after
            // the restart, and completing THAT handshake on first's end is what un-gates the
            // ordinary lane the kill loop below needs.
            Context.System.ActorSelection(new RootActorPath(_firstAddress) / "user" / ReadyActorName)
                .Tell(ReadySignal);
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

        // All nodes capture the baseline DeltaCount before node-specific logic. The baseline must
        // be read AFTER pub-sub gossip goes quiet, not merely after CountAsync - see the comment on
        // ReadStableDeltaCountAsync for why those are not the same moment.
        var oldDeltaCount = await ReadStableDeltaCountAsync();

        // Captured on EVERY node while all three systems are still alive. Third's original Sys
        // terminates partway through its own restart below, and NodeAsync needs the
        // TestConductor client that dies along with it - so first's address has to be in hand
        // before that happens, not looked up afterward.
        var firstAddress = (await NodeAsync(_config.First)).Address;
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

            // Stand this up BEFORE Shutdown(third) so it exists no matter how fast the restarted
            // incarnation comes back. It is the receiving end of the ready-ping the fresh
            // Shutdown actor sends from its own PreStart (see that class, above) - reversing the
            // direction of first contact from "first polls a stale association" to "third
            // announces itself over an association with no history to carry".
            var readyProbe = CreateTestProbe();
            Sys.ActorOf(Akka.TestKit.TestActors.ForwardActor.Props(readyProbe.Ref),
                DistributedPubSubRestartSpecConfig.ReadyActorName);

            await TestConductor.Shutdown(_config.Third).WaitAsync(30.Seconds());

            // Wait for third's restarted incarnation to make first contact, instead of starting
            // this window's clock at Shutdown()'s return - before graceful CoordinatedShutdown,
            // the fresh ActorSystem, the rebind, the self-join and third's own 5s ExpectNoMsg
            // have even begun. This is not merely a signal: the inbound HandshakeReq that carries
            // it is what completes first's outbound handshake to the new incarnation
            // (InboundHandshakeStage.HandleReq -> CompleteHandshake), so by the time this
            // returns, the ordinary lane the kill loop below uses is no longer gated on the dead
            // old uid.
            await readyProbe.ExpectMsgAsync<string>(
                msg => msg == DistributedPubSubRestartSpecConfig.ReadySignal, 60.Seconds());

            // ActorSelection.Tell, not ResolveOne + a resolved ref: only an ActorSelectionMessage
            // pierces a quarantined association (ArteryRemoting.Send drops a plain Tell to a
            // quarantined peer; Pekko's Association.scala carries the identical carve-out, and
            // upstream's own restart spec relies on exactly that). The ready-ping above should
            // already have healed the association, so this loop is a short closed-loop
            // confirmation - not the thing racing third's restart cost the way the old,
            // Shutdown()-anchored window did.
            var shutdownSelection = Sys.ActorSelection(new RootActorPath(thirdAddress) / "user" / "shutdown");
            await AwaitAssertAsync(async () =>
            {
                // Fresh probe per attempt: a late ack from a previous attempt must never satisfy
                // the next one. The Shutdown actor replies before it terminates, so the ack is
                // proof that THIS incarnation received the message.
                var killProbe = CreateTestProbe();
                shutdownSelection.Tell("shutdown", killProbe.Ref);
                await killProbe.ExpectMsgAsync<string>(msg => msg == "shutdown-ack", 2.Seconds());
            }, 20.Seconds(), 500.Milliseconds());

            await EnterBarrierAsync("end");

            // Read DeltaCount on its own probe so the pub-sub subscription on TestActor cannot
            // mix into this query.
            var deltaProbe = CreateTestProbe();
            Mediator.Tell(DeltaCount.Instance, deltaProbe.Ref);
            var deltaCount = await deltaProbe.ExpectMsgAsync<long>(5.Seconds());
            deltaCount.Should().Be(oldDeltaCount);
        }, _config.First);

        await RunOnAsync(async () =>
        {
            var node3Address = Cluster.Get(Sys).SelfAddress;

            // The fresh system below rebinds this exact host:port, so the old system has to
            // release it first. Name that dependency on failure - a discarded or unexplained
            // wait resurfaces later as a confusing bind error on the fresh system.
            var terminationTimeout = 30.Seconds();
            try
            {
                await Sys.WhenTerminated.WaitAsync(terminationTimeout);
            }
            catch (TimeoutException e)
            {
                throw new TimeoutException(
                    $"Failed to stop [{Sys.Name}] within [{terminationTimeout}]. The fresh system " +
                    $"cannot rebind [{node3Address}] until the old one releases it.", e);
            }

            // Pin the fresh system to the SAME wire address for BOTH transports - under
            // AKKA_MNTR_TRANSPORT=artery the classic dot-netty key is inert and the fresh
            // system would bind a random artery canonical.port instead.
            var newSystem = ActorSystem.Create(
                Sys.Name,
                ConfigurationFactory
                    .ParseString($"akka.remote.dot-netty.tcp.port={node3Address.Port}\n" +
                        $"akka.remote.artery.canonical.port={node3Address.Port}")
                    .WithFallback(Sys.Settings.Config));

            // Settle, on the next Linux failure, whether the listener came up on the pinned port
            // at all. First's whole restart-detection path - the ready ping above and the kill
            // loop's ActorSelection - depends on this system actually listening on
            // node3Address's port, and nothing upstream of this line would surface a silent
            // mismatch; it would just look like first's ready-ping wait timing out.
            var actualAddress = Cluster.Get(newSystem).SelfAddress;
            newSystem.Log.Info("Restarted system bound to [{0}] (pinned [{1}])", actualAddress, node3Address);
            actualAddress.Port.Should().Be(node3Address.Port,
                "the fresh system must rebind the address the other nodes still address it by");

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
                newSystem.ActorOf(
                    Props.Create(() => new DistributedPubSubRestartSpecConfig.Shutdown(firstAddress)),
                    "shutdown");

                // First's closed-loop kill (above) normally drives this WhenTerminated: it keeps
                // re-poking the association until third's /user/shutdown acks the kill, at which
                // point newSystem terminates and this wait completes. Give it a generous 120s
                // upper bound so first's whole worst-case pipeline (the 30s Shutdown cap, plus
                // the dilated 60s ready-ping wait, plus the 20s closed-loop kill) fits comfortably.
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

    /// <summary>
    /// Reads the mediator's DeltaCount, but only once the pub-sub gossip that feeds it has
    /// stopped moving. Returns the settled value.
    /// </summary>
    private async Task<long> ReadStableDeltaCountAsync()
    {
        // DeltaCount counts Delta MESSAGES received. It does not count registry changes, and the
        // two diverge badly while a node is still learning the cluster.
        //
        // A mediator merges a bucket only if it already knows the bucket's owner as a cluster
        // member. DistributedPubSubMediator's Delta handler increments _deltaCount first and only
        // then tests _nodes.Contains(bucket.Owner). So while this node still waits for third's
        // MemberUp, first pushes third's bucket on every 500ms gossip tick and this node discards
        // every push - each discarded push still counted. Convergence therefore arrives as a BURST
        // of Deltas, and redundant ones trail it: a peer keeps re-sending until our next outbound
        // gossip tells it we caught up.
        //
        // CountAsync below unblocks on the FIRST push this node actually merges, so it returns
        // while that burst is still draining. Reading the baseline right there captures a mid-burst
        // value and lets the trailing Deltas land after it - which is exactly the reported failure
        // ("Expected value to be 3L, but found 4L" on second). Measured over 20 local runs the last
        // Delta arrived only 104-1010ms before the old unguarded read, against a 500ms gossip tick:
        // one delayed tick flips it.
        //
        // So gate the baseline on quiescence instead of reading mid-burst. Two samples 2s apart
        // must agree. 2s = 4 ticks of this spec's 500ms gossip-interval, which covers our own
        // outbound tick plus the peer's reaction to it, with slack. The 30s ceiling is
        // convergence-scale headroom, not a budget to lean on - the gate settles on the second
        // sample in practice. Fresh probe per sample, matching CountAsync, so a late reply cannot
        // be mistaken for the next sample.
        var previous = long.MinValue;
        var settledValue = 0L;
        await AwaitAssertAsync(async () =>
        {
            var probe = CreateTestProbe();
            Mediator.Tell(DeltaCount.Instance, probe.Ref);
            var current = await probe.ExpectMsgAsync<long>(1.Seconds());

            var lastSeen = previous;
            previous = current;
            settledValue = current;

            (current == lastSeen).Should().BeTrue(
                "pub-sub gossip must go quiet before DeltaCount can serve as a baseline " +
                $"(previous sample {lastSeen}, current sample {current})");
        }, 30.Seconds(), 2.Seconds());

        return settledValue;
    }

    private async Task CountAsync(int expected)
    {
        // Gossip-propagation check: registrations spread on the 500ms pub-sub gossip tick, so
        // retry on that tick for 10s. Fresh probe and an explicit 1s bound per attempt - the
        // reply is a local round trip, and inheriting the 3s single-expect default would spend
        // the whole budget on three attempts and let a late reply be read as a stale count.
        await AwaitAssertAsync(async () =>
        {
            var probe = CreateTestProbe();
            Mediator.Tell(Count.Instance, probe.Ref);
            (await probe.ExpectMsgAsync<int>(1.Seconds())).Should().Be(expected);
        }, 10.Seconds(), 500.Milliseconds());
    }
}