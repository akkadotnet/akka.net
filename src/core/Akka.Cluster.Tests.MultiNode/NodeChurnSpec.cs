//-----------------------------------------------------------------------
// <copyright file="NodeChurnSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;

namespace Akka.Cluster.Tests.MultiNode
{
    public class NodeChurnConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public NodeChurnConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString(@"
                  akka.cluster.run-coordinated-shutdown-when-down = on
                  # MultiNodeSpec.BaseConfig sets cluster.downing-provider-class to empty
                  # (MultiNodeSpec.cs:420), so ANY value here selects the legacy AutoDowning provider
                  # (ClusterSettings.cs:95-103), not the keep-majority SBR that Cluster.cs:128-130
                  # promises in its warning. AutoDowning has no quorum logic: both sides of a
                  # partition down each other and neither heals.
                  #
                  # This spec does not need a downing provider. It removes every transient member with
                  # an explicit Leave (odd rounds) or Down (even rounds) and waits for the removal in
                  # AwaitRemovedAsync. The leader removes a Down or Exiting member as soon as it is
                  # unreachable or terminated (ClusterDaemon.cs:2345-2348 with
                  # MembershipState.RemoveUnreachableWithMemberStatus), which needs only the failure
                  # detector.
                  #
                  # Pekko's NodeChurnSpec keeps auto-down-unreachable-after = 1s; this port turns it
                  # off on purpose, not by omission. Legacy auto-down is a second, uncoordinated
                  # downing path with no quorum logic of its own, so leaving it on here would race the
                  # explicit Leave/Down calls above with an independent auto-down decision. With it
                  # off, the widened acceptable-heartbeat-pause below is the only guard against the
                  # measured 5.628s heartbeat stall - there is no second downing path left to catch
                  # what it misses.
                  akka.cluster.auto-down-unreachable-after = off

                  # Only guard against that stall now that auto-down above is off.
                  # MultiNodeClusterSpec.ClusterConfig() sets heartbeat-interval = 500ms but leaves
                  # acceptable-heartbeat-pause at the 3s default (Cluster.conf:219), and
                  # akka.test.timefactor does not dilate failure-detector settings. Measured: a
                  # transient-system teardown produced a 5.628s heartbeat gap, and detection fired
                  # 3.971s after the last heartbeat. 10s moves detection to about 10.97s, a margin of
                  # 5.34s over the worst gap observed. This is a stopgap for a product-side blocking
                  # wait in the scheduler shutdown path; it can come back down once issue #8549 is
                  # addressed.
                  akka.cluster.failure-detector.acceptable-heartbeat-pause = 10s

                  # This spec asserts the ABSENCE of gossip-payload growth (ExpectNoMsgAsync after
                  # each round, driven by the LogListener below). Without this setting, tombstones for
                  # removed members never prune inside a 5-round run (Cluster.conf:151 defaults to
                  # 24h), so their vector-clock entries keep accumulating and a working payload
                  # listener would eventually fire and fail the test. Pruning at 1s is what lets the
                  # no-growth assertion hold across all five rounds. Matches Pekko.
                  akka.cluster.prune-gossip-tombstones-after = 1s
                  akka.remote.log-frame-size-exceeding = 2000b
                  akka.remote.dot-netty.tcp.batching.enabled = false # disable batching
                "))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class NodeChurnSpec : MultiNodeClusterSpec
    {
        private class LogListener : ReceiveActor
        {
            private readonly IActorRef _testActor;

            public LogListener(IActorRef testActor)
            {
                _testActor = testActor;

                // info.Message is a LogMessage, never a string. RemoteMetricsExtension.cs:109 logs a
                // format plus two arguments, and ILoggingAdapter.cs:56-61 wraps that in
                // LogMessage<LogValues<T1, T2>>. The old `info.Message is string` guard never matched,
                // so every Info went unhandled - visible in CI as "DeadLetter from
                // [.../cluster/core/daemon] to [/user/logListener]" - and the ExpectNoMsg assertions
                // this spec exists for could never fail.
                // RemoteMetricsSpec.cs:118 already uses the correct form.
                //
                // The prefix must be built from the .NET type, not carried over from the JVM class
                // name. RemoteMetricsExtension.cs:109 logs type.FullName, and GossipEnvelope
                // (Akka.Cluster, internal - Akka.Cluster has InternalsVisibleTo this assembly) is
                // "Akka.Cluster.GossipEnvelope", not "akka.cluster.GossipEnvelope". String.StartsWith
                // is case-sensitive, so a literal JVM-cased prefix never matches and this listener
                // could never forward anything, leaving the payload assertions below unable to fail
                // either.
                //
                // Under Artery there is no RemoteMetricsExtension at all (issue #8555), so this
                // listener - and the payload-growth assertions it feeds - cannot fire on the Artery
                // lane regardless of this fix. Only the classic (DotNetty) transport exercises them.
                var payloadSizePrefix = "New maximum payload size for [" + typeof(GossipEnvelope).FullName + "]";
                Receive<Info>(info =>
                {
                    var text = info.Message?.ToString();
                    if (text is not null && text.StartsWith(payloadSizePrefix))
                    {
                        _testActor.Tell(text);
                    }
                });
            }
        }

        private readonly NodeChurnConfig _config;
        private const int Rounds = 5;

        private ImmutableList<Address> SeedNodes
        {
            get
            {
                return ImmutableList.Create(GetAddress(_config.First), GetAddress(_config.Second), GetAddress(_config.Third));
            }
        }

        public NodeChurnSpec() : this(new NodeChurnConfig())
        {
        }

        protected NodeChurnSpec(NodeChurnConfig config) : base(config, typeof(NodeChurnSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public async Task NodeChurnSpecs()
        {
            await Cluster_with_short_lived_members_must_setup_stable_nodes();
            await Cluster_with_short_lived_members_must_join_and_remove_transient_nodes_without_growing_gossip_payload();
        }

        public async Task Cluster_with_short_lived_members_must_setup_stable_nodes()
        {
            await WithinAsync(15.Seconds(), async () =>
            {
                var logListener = Sys.ActorOf(Props.Create(() => new LogListener(TestActor)), "logListener");
                Sys.EventStream.Subscribe(logListener, typeof(Info));
                Cluster.JoinSeedNodes(SeedNodes);
                await AwaitMembersUpAsync(Roles.Count);
                await EnterBarrierAsync("stable");
            });
        }

        public async Task Cluster_with_short_lived_members_must_join_and_remove_transient_nodes_without_growing_gossip_payload()
        {
            // This test is configured with log-frame-size-exceeding and the LogListener
            // will send to the testActor if unexpected increase in message payload size.
            // It will fail after a while if vector clock entries of removed nodes are not pruned.
            for (var n = 1; n <= Rounds; n++)
            {
                Log.Info("round-" + n);
                var systems = Enumerable.Repeat(0,2).Select(_ => ActorSystem.Create(Sys.Name, Sys.Settings.Config)).ToImmutableList();

                foreach (var s in systems)
                {
                    MuteDeadLetters(s);
                    Cluster.Get(s).JoinSeedNodes(SeedNodes);
                }

                await AwaitAllMembersUpAsync(systems);
                await EnterBarrierAsync("members-up-" + n);

                foreach (var node in systems)
                {
                    if (n % 2 == 0)
                    {
                        Cluster.Get(node).Down(Cluster.Get(node).SelfAddress);
                    }
                    else
                    {
                        Cluster.Get(node).Leave(Cluster.Get(node).SelfAddress);
                    }
                }

                await AwaitRemovedAsync(systems, n);
                await EnterBarrierAsync("members-removed-" + n);

                // Terminate the transient systems asynchronously and concurrently. #8286 replaced the
                // synchronous Shutdown(node, verifySystemShutdown:true) helper, which pinned a
                // thread-pool thread inside Task.Wait(); awaiting Terminate() frees that thread and
                // WaitAsync keeps the verify-shutdown semantics by throwing TimeoutException if a
                // system fails to stop in time.
                //
                // Do NOT change this to a sequential `foreach (var s in systems) await s.Terminate();`.
                // It looks like it would halve the teardown concurrency and it does not. Under
                // MultiNodeSpec.BaseConfig, Terminate() calls FinalTerminate() directly
                // (coordinated-shutdown.run-by-actor-system-terminate = off, MultiNodeSpec.cs:406), and
                // the task it returns is the LAST-registered termination callback, not the last one to
                // run (ActorSystemImpl.cs:554, 653-700). StopScheduler was registered first, so it runs
                // AFTER the awaited task completes and nobody waits for it. Awaiting one system at a
                // time therefore leaves that system's blocking scheduler shutdown overlapping the next
                // one anyway. Sequential teardown moves none of it.
                // 20s, not 30s: the barrier immediately below has its own 30s timeout
                // (Akka.Remote.TestKit/Internals/Reference.conf:13, barrier-timeout), and that clock
                // arms at the first node's arrival, not at the last (BarrierCoordinator.cs:570-578). A
                // 30s teardown budget equal to the barrier's own 30s timeout would leave zero skew
                // margin between a node that finishes teardown quickly and a peer that uses its full
                // budget - the slow peer would hit the barrier timeout at the same instant it arrives.
                // 20s keeps 10s of margin before the barrier's own deadline.
                await Task.WhenAll(systems.Select(s => s.Terminate())).WaitAsync(20.Seconds());

                // Pekko has enterBarrier("end-round-" + n) here; the .NET port never did (checked back
                // to the original port, 1c1ced42a). Without it, a node that finishes teardown early
                // starts creating two fresh ActorSystems while its peers still hold two dying ones.
                await EnterBarrierAsync("end-round-" + n);

                Log.Info("end of round-" + n);
                // log listener will send to testActor if payload size exceed configured log-frame-size-exceeding
                await ExpectNoMsgAsync(2.Seconds());
            }
            await ExpectNoMsgAsync(5.Seconds());
        }

        private async Task AwaitAllMembersUpAsync(ImmutableList<ActorSystem> additionalSystems)
        {
            var numberOfMembers = Roles.Count + Roles.Count * additionalSystems.Count;
            await AwaitMembersUpAsync(numberOfMembers);
            await WithinAsync(20.Seconds(), async () =>
            {
                await AwaitAssertAsync(() =>
                {
                    additionalSystems.ForEach(s =>
                    {
                        var cluster = Cluster.Get(s);
                        cluster.State.Members.Count.Should().Be(numberOfMembers);
                        cluster.State.Members.All(c => c.Status == MemberStatus.Up).Should().BeTrue("All members should be up.");
                    });
                });
            });
        }

        private async Task AwaitRemovedAsync(ImmutableList<ActorSystem> additionalSystems, int round)
        {
            await AwaitMembersUpAsync(Roles.Count, timeout: 40.Seconds());
            await EnterBarrierAsync("removed-" + round);
            await WithinAsync(3.Seconds(), async () =>
            {
                await AwaitAssertAsync(() =>
                {
                    additionalSystems.ForEach(s =>
                    {
                        Cluster.Get(s).IsTerminated.Should().BeTrue($"{Cluster.Get(s).SelfAddress}");
                    });
                });
            });
        }
    }
}
