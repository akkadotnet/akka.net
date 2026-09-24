//-----------------------------------------------------------------------
// <copyright file="RemotingTerminatorSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.TestKit.TestActors;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests
{
    /// <summary>
    /// Validate that the remoting terminator for Akka.Remote transports fires and does not
    /// indefinitely block the <see cref="ActorSystem"/> from terminating.
    /// </summary>
    public class RemotingTerminatorSpecs : AkkaSpec
    {
        public static readonly Config RemoteConfig = ConfigurationFactory.ParseString(@"
            akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            akka.remote.dot-netty.tcp {
                hostname = 127.0.0.1
                port = 0
            }
            # A remote-deployed actor's Terminated normally arrives in milliseconds over the graceful
            # disassociation, but that delivery is best-effort (nothing waits for an ack before the
            # channel closes). When it's lost, the watch failure detector is what reports the death;
            # keep it well inside the tests' 10 s windows instead of the 10 s default pause.
            akka.remote.watch-failure-detector.acceptable-heartbeat-pause = 2s
        ");

        /// <summary>
        /// Watches a remote-deployed actor and waits until the watch is registered on the wire: the
        /// TestActor's Watch becomes a WatchRemote for the local RemoteWatcher, so an Identify round trip
        /// with the RemoteWatcher orders its remote Watch ahead of the Identify sent to the actor.
        /// </summary>
        private async Task WatchRemoteDeployedAsync(IActorRef remoteDeployed)
        {
            await WatchAsync(remoteDeployed);
            await Sys.ActorSelection("/system/remote-watcher").Ask<ActorIdentity>(new Identify(null), RemainingOrDefault);
        }

        private ActorSystem _sys2;
        
        public RemotingTerminatorSpecs(ITestOutputHelper output) : base(RemoteConfig, output) { }

        protected override void AfterAll()
        {
            base.AfterAll();
            if (_sys2 != null)
                Shutdown(_sys2);
        }

        [Fact]
        public async Task RemotingTerminator_should_shutdown_promptly_with_no_associations()
        {
            await WithinAsync(TimeSpan.FromSeconds(10), async () =>
            {
                Sys.EventStream.Subscribe(TestActor, typeof (RemotingShutdownEvent));
                Sys.EventStream.Subscribe(TestActor, typeof (RemotingErrorEvent));
                Assert.True(await Sys.Terminate().AwaitWithTimeout(RemainingOrDefault), "Expected to terminate within 10 seconds, but didn't.");
            });
        }

        [Fact]
        public async Task RemotingTerminator_should_shutdown_promptly_with_some_associations()
        {
            _sys2 = ActorSystem.Create("System2", RemoteConfig);
            InitializeLogger(_sys2);
            var sys2Address = RARP.For(_sys2).Provider.DefaultAddress;

            // open an association
            var associated = await Sys.ActorSelection(new RootActorPath(sys2Address)/"system"/"remote-watcher")
                .ResolveOne(TimeSpan.FromSeconds(4));

            Sys.EventStream.Subscribe(TestActor, typeof(RemotingShutdownEvent));
            Sys.EventStream.Subscribe(TestActor, typeof(RemotingErrorEvent));
            var terminationTask = Sys.Terminate();
            await ExpectMsgAsync<RemotingShutdownEvent>();
            Assert.True(await terminationTask.AwaitWithTimeout(10.Seconds()), "Expected to terminate within 10 seconds, but didn't.");

            // now terminate the second system
            Assert.True(await _sys2.Terminate().AwaitWithTimeout(10.Seconds()), "Expected to terminate within 10 seconds, but didn't.");
        }

        [Fact]
        public async Task RemotingTerminator_should_shutdown_properly_with_remotely_deployed_actor()
        {
            _sys2 = ActorSystem.Create("System2", RemoteConfig);
            InitializeLogger(_sys2);
            var sys2Address = RARP.For(_sys2).Provider.DefaultAddress;

            // open an association via remote deployment
            var associated =
                Sys.ActorOf(BlackHoleActor.Props.WithDeploy(Deploy.None.WithScope(new RemoteScope(sys2Address))),
                    "remote");
            await WatchRemoteDeployedAsync(associated);

            // verify that the association is open (don't terminate until handshake is finished)
            var actorIdentity = await associated.Ask<ActorIdentity>(new Identify("foo"), RemainingOrDefault);
            actorIdentity.MessageId.ShouldBe("foo");

            // terminate the DEPLOYED system and observe the remote-deployed actor's death, by either the
            // graceful disassociation or the watch failure detector (see RemoteConfig)
            await WithinAsync(TimeSpan.FromSeconds(10), async () =>
            {
                var terminationTask = _sys2.Terminate(); // start termination of the deployed system
                await ExpectTerminatedAsync(associated);  // expect that the remote deployed actor is dead
                Assert.True(await terminationTask.AwaitWithTimeout(RemainingOrDefault), "Expected to terminate within 10 seconds, but didn't.");
            });

            // now terminate the DEPLOYER system
            Assert.True(await Sys.Terminate().AwaitWithTimeout(10.Seconds()), "Expected to terminate within 10 seconds, but didn't.");
        }

        [Fact]
        public async Task RemotingTerminator_should_shutdown_properly_without_exception_logging_while_graceful_shutdown()
        {
            await EventFilter.Exception<ShutDownAssociation>().ExpectAsync(0,
                async () =>
                {
                    _sys2 = ActorSystem.Create("System2", RemoteConfig);
                    InitializeLogger(_sys2);
                    var sys2Address = RARP.For(_sys2).Provider.DefaultAddress;

                    // open an association via remote deployment
                    var associated = Sys.ActorOf(BlackHoleActor.Props.WithDeploy(Deploy.None.WithScope(new RemoteScope(sys2Address))), "remote");

                    await WatchRemoteDeployedAsync(associated);

                    // verify that the association is open (don't terminate until handshake is finished)
                    (await associated.Ask<ActorIdentity>(new Identify("foo"), RemainingOrDefault)).MessageId.ShouldBe("foo");

                    // terminate the DEPLOYED system
                    await WithinAsync(TimeSpan.FromSeconds(10), async () =>
                    {
                        var terminationTask = _sys2.Terminate(); // start termination process
                        await ExpectTerminatedAsync(associated);  // expect that the remote deployed actor is dead
                        Assert.True(await terminationTask.AwaitWithTimeout(RemainingOrDefault), "Expected to terminate within 10 seconds, but didn't.");
                    });

                    // now terminate the DEPLOYER system
                    Assert.True(await Sys.Terminate().AwaitWithTimeout(10.Seconds()), "Expected to terminate within 10 seconds, but didn't.");
                });
        }
    }
}
