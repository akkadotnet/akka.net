//-----------------------------------------------------------------------
// <copyright file="RemoteDeploymentFlushOnShutdownSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Transport;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests
{
    /// <summary>
    /// Repro/characterization spec for the shutdown-time "flush remote deployments" behavior on the
    /// default DotNetty transport.
    ///
    /// This is the in-process analog of the multi-node <c>RemoteDeploymentDeathWatchSpec</c>: a
    /// "deployer" ActorSystem remotely deploys an actor onto a "host" ActorSystem. The host is then made
    /// permanently unreachable via a bidirectional transport blackhole (the sanctioned in-process way to
    /// simulate a hard node crash / network partition on DotNetty — no graceful Akka Disassociate ever
    /// reaches the deployer, exactly like <c>TestConductor.Exit(node, 0)</c> in the multi-node test).
    /// Finally the deployer is terminated via <see cref="ActorSystem.Terminate"/>.
    ///
    /// The deployer's <see cref="RemoteDeploymentWatcher"/> is watching the remote child on behalf of the
    /// local supervisor (the /user guardian). Without the shutdown-time flush, that supervisor blocks
    /// forever during shutdown waiting for the (now unreachable) remote child to confirm termination — the
    /// remote DeathWatch round-trip can never complete once the deployer's own remoting is tearing down —
    /// so <see cref="ActorSystem.WhenTerminated"/> never completes. With the flush, the watcher proactively
    /// releases the supervisor at <c>PhaseBeforeActorSystemTerminate</c> and the deployer terminates
    /// promptly.
    ///
    /// The watch failure detector is deliberately widened so it cannot mask the bug by eventually
    /// declaring the host unreachable inside the test window.
    /// </summary>
    public class RemoteDeploymentFlushOnShutdownSpec : AkkaSpec
    {
        public RemoteDeploymentFlushOnShutdownSpec(ITestOutputHelper output)
            : base("akka.loglevel = WARNING", output)
        {
        }

        private static Config RemoteConfig() => ConfigurationFactory.ParseString(@"
            akka {
                actor.provider = remote
                remote.dot-netty.tcp {
                    hostname = 127.0.0.1
                    port = 0
                    # trttl adapter lets us blackhole the association in-process to simulate a hard
                    # host crash. The underlying transport is still DotNetty TCP.
                    applied-adapters = [""trttl""]
                }
                # Widen remote-watch failure detection so it CANNOT rescue a hung shutdown within the
                # test window. The only thing that should let the deployer terminate is the flush.
                remote.watch-failure-detector.acceptable-heartbeat-pause = 60 s
            }");

        private sealed class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        [Fact(DisplayName = "Deployer ActorSystem must fully terminate promptly after the remote deployment host crashes (DotNetty)")]
        public async Task Deployer_must_shut_down_promptly_after_remote_host_crash()
        {
            var host = ActorSystem.Create("Host", RemoteConfig());
            var hostAddress = RARP.For(host).Provider.DefaultAddress;

            var deployerConfig = ConfigurationFactory.ParseString(
                    $@"akka.actor.deployment./hello.remote = ""{hostAddress}""")
                .WithFallback(RemoteConfig());
            var deployer = ActorSystem.Create("Deployer", deployerConfig);

            try
            {
                // remotely deploy /hello onto the host node
                var hello = deployer.ActorOf(Props.Create<Echo>(), "hello");

                // location-transparency check: the child physically lives on the host
                hello.Path.Address.ShouldBe(hostAddress);

                // confirm the remote deployment is live via a round-trip through the host
                (await hello.Ask<string>("ping", TimeSpan.FromSeconds(15))).ShouldBe("ping");

                // HARD CRASH the host: bidirectional blackhole. No graceful Disassociate ever reaches the
                // deployer, so it cannot clean up the remote child through the normal DeathWatch path.
                var deployerTransport = RARP.For(deployer).Provider.Transport;
                var hostNaked = new Address("akka", host.Name, hostAddress.Host, hostAddress.Port!.Value);
                (await deployerTransport.ManagementCommand(
                    new SetThrottle(hostNaked, ThrottleTransportAdapter.Direction.Both, Blackhole.Instance)))
                    .ShouldBeTrue("SetThrottle(Blackhole) command was not accepted by the transport");

                // verify the blackhole actually took effect: a round-trip must now time out
                var reachedAfterBlackhole = true;
                try
                {
                    await hello.Ask<string>("ping-after-blackhole", TimeSpan.FromSeconds(3));
                }
                catch
                {
                    reachedAfterBlackhole = false;
                }

                Assert.False(reachedAfterBlackhole,
                    "Blackhole did not take effect — the host is still reachable, so the test setup is invalid.");

                // Shut the deployer down and require it to FULLY terminate inside a tight bound.
                // NB: we must assert on WhenTerminated, not on Terminate()'s task: a CoordinatedShutdown
                // phase that hangs still lets Terminate()'s task complete on the phase timeout while the
                // system remains alive.
                var sw = Stopwatch.StartNew();
                _ = deployer.Terminate();
                var terminated = await Task.WhenAny(deployer.WhenTerminated, Task.Delay(TimeSpan.FromSeconds(10)));
                sw.Stop();

                Assert.True(ReferenceEquals(terminated, deployer.WhenTerminated) && deployer.WhenTerminated.IsCompleted,
                    $"Deployer ActorSystem did not fully terminate within 10s after the remote host crashed " +
                    $"(elapsed {sw.Elapsed}). Its /user guardian is still blocked waiting for the unreachable " +
                    $"remote child — the remote deployment was not flushed at shutdown.");

                Log.Info("Deployer fully terminated in {0}", sw.Elapsed);
            }
            finally
            {
                ((ExtendedActorSystem)deployer).Abort();
                ((ExtendedActorSystem)host).Abort();
            }
        }
    }
}
