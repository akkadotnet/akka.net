//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonRestartSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tools.Tests.Singleton
{
    public class ClusterSingletonRestartSpec : AkkaSpec
    {
        private readonly ActorSystem _sys1;
        private readonly ActorSystem _sys2;
        private ActorSystem _sys3 = null;

        public ClusterSingletonRestartSpec(ITestOutputHelper output) : base(@"
              akka.loglevel = INFO
              akka.actor.provider = ""cluster""
              akka.remote {
                dot-netty.tcp {
                  hostname = ""127.0.0.1""
                  port = 0
                }
              }", output)
        {
            _sys1 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            _sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);

            // route the other systems' logs into the test output, the way ClusterSingletonRestart2Spec
            // does - without them a CI failure here shows nothing about the cluster that caused it
            InitializeLogger(_sys1);
            InitializeLogger(_sys2);
        }

        public async Task JoinAsync(ActorSystem from, ActorSystem to)
        {
            from.ActorOf(ClusterSingletonManager.Props(Echo.Props,
                PoisonPill.Instance,
                ClusterSingletonManagerSettings.Create(from)), "echo");

            var fromCluster = Cluster.Get(from);
            var toAddress = Cluster.Get(to).SelfAddress;

            // The join is re-issued on every attempt because sys3 reuses sys1's host:port: the target
            // refuses it until the previous incarnation has been downed and removed, and a single Join
            // would then sit out akka.cluster.retry-unsuccessful-join-after (10s). Half a second between
            // attempts covers a gossip round trip; the old 100ms cadence re-sent JoinTo ten times a
            // second, and a JoinTo received while the daemon is still in TryingToJoin drops it back to
            // Uninitialized and restarts the handshake (ClusterDaemon.cs:1273), so the retry itself was
            // adding load to the daemon it was waiting on.
            // Both assertions read one members snapshot - the original read Cluster.State twice and
            // could see two different gossip versions inside a single attempt.
            await AwaitAssertAsync(() =>
            {
                fromCluster.Join(toAddress);
                var members = fromCluster.State.Members;
                members.Select(x => x.UniqueAddress).Should().Contain(fromCluster.SelfUniqueAddress);
                members.Select(x => x.Status)
                    .ToImmutableHashSet()
                    .Should()
                    .Equal(ImmutableHashSet<MemberStatus>.Empty.Add(MemberStatus.Up));
            }, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(500));
        }

        // Sends a message through the proxy until the singleton answers. The send is retried because the
        // proxy buffers or drops traffic while it holds no singleton reference, but the probe is built
        // once. CreateTestProbe blocks the caller until the probe's PreStart has run on the test-actor
        // dispatcher (TestKitBase.cs:739) and replaces the calling thread's SynchronizationContext
        // (TestKitBase.cs:194), so one per attempt puts a blocking wait, a context swap and a leaked
        // system actor inside the retry loop - on a starved agent that wait is competing for the very
        // thread that has to run the probe it is waiting for.
        private async Task AwaitProxyReplyAsync(ActorSystem system, IActorRef proxy, string message, TimeSpan max)
        {
            var probe = CreateTestProbe(system);
            await AwaitAssertAsync(async () =>
            {
                proxy.Tell(message, probe.Ref);
                await probe.ExpectMsgAsync(message, TimeSpan.FromSeconds(1));
            }, max);
        }

        [Fact]
        public async Task Restarting_cluster_node_with_same_hostname_and_port_must_handover_to_next_oldest()
        {
            await JoinAsync(_sys1, _sys1);
            await JoinAsync(_sys2, _sys1);

            var proxy2 = _sys2.ActorOf(
                ClusterSingletonProxy.Props("user/echo", ClusterSingletonProxySettings.Create(_sys2)), "proxy2");

            await AwaitProxyReplyAsync(_sys2, proxy2, "hello", TimeSpan.FromSeconds(5));

            // Await the graceful stop rather than TestKit's Shutdown helper. That one blocks the calling
            // thread on Terminate().Wait(Dilated(5s)) and then silently force-stops the user guardian -
            // which does not stop /system, so remoting keeps sys1's listener bound while sys3 is created
            // on the same host:port below. dot-netty's tcp-reuse-addr is off-for-windows, so that rebind
            // is exactly the kind of thing that fails on Windows and nowhere else. Terminate() runs
            // CoordinatedShutdown to completion: the hand-over to sys2 finishes and the port is released.
            await _sys1.Terminate();
            // it will be downed by the join attempts of the new incarnation

            // ReSharper disable once PossibleInvalidOperationException
            var sys1Port = Cluster.Get(_sys1).SelfAddress.Port.Value;
            var sys3Config = ConfigurationFactory.ParseString(@"akka.remote.dot-netty.tcp.port=" + sys1Port)
                .WithFallback(_sys1.Settings.Config);
            _sys3 = ActorSystem.Create(_sys1.Name, sys3Config);
            InitializeLogger(_sys3);

            await JoinAsync(_sys3, _sys2);

            // JoinAsync only proves sys3's own view. sys2 has to see sys3 reach Up as well, because
            // sys2's singleton manager picks its hand-over target from its own member list. If sys2
            // leaves while it still sees sys3 as Joining there is no target, and the cluster-exiting
            // CoordinatedShutdown phase runs to its 10s timeout before sys2 can be removed - which is
            // what puts the removal assertion below over its budget.
            await AwaitAssertAsync(() =>
            {
                foreach (var system in new[] { _sys2, _sys3 })
                {
                    var members = Cluster.Get(system).State.Members;
                    members.Select(x => x.Status)
                        .ToImmutableHashSet()
                        .Should()
                        .Equal(ImmutableHashSet<MemberStatus>.Empty.Add(MemberStatus.Up));
                    members.Count.Should().Be(2);
                }
            }, TimeSpan.FromSeconds(10));

            await AwaitProxyReplyAsync(_sys2, proxy2, "hello2", TimeSpan.FromSeconds(5));

            Cluster.Get(_sys2).Leave(Cluster.Get(_sys2).SelfAddress);

            await AwaitAssertAsync(() =>
            {
                Cluster.Get(_sys3)
                    .State.Members.Select(x => x.UniqueAddress)
                    .Should()
                    .Equal(Cluster.Get(_sys3).SelfUniqueAddress);
            }, TimeSpan.FromSeconds(15));

            var proxy3 =
                _sys3.ActorOf(ClusterSingletonProxy.Props("user/echo", ClusterSingletonProxySettings.Create(_sys3)),
                    "proxy3");

            await AwaitProxyReplyAsync(_sys3, proxy3, "hello3", TimeSpan.FromSeconds(5));
        }

        protected override void AfterAll()
        {
            base.AfterAll();
            Shutdown(_sys1);
            Shutdown(_sys2);
            if(_sys3 != null)
                Shutdown(_sys3);
        }

        /// <summary>
        /// NOTE:
        /// For some reason the built-in <see cref="EchoActor"/> is over complicated and
        /// doesn't just reply to the sender, but also replies to <see cref="TestActor"/> as well.
        /// 
        /// Created this so we have something simple to work with.
        /// </summary>
        public class Echo : ReceiveActor
        {
            public static Props Props = Props.Create(() => new Echo());

            public Echo()
            {
                ReceiveAny(o =>
                {
                    Sender.Tell(o);
                });
            }
        }
    }
}
