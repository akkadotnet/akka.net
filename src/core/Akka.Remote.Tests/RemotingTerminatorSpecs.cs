//-----------------------------------------------------------------------
// <copyright file="RemotingTerminatorSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Xunit;
using Xunit.Abstractions;

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
        ");

        public RemotingTerminatorSpecs(ITestOutputHelper output) : base(RemoteConfig, output) { }

        [Fact]
        public void RemotingTerminator_should_shutdown_promptly_with_no_associations()
        {
            Within(TimeSpan.FromSeconds(10), () =>
            {
                Sys.EventStream.Subscribe(TestActor, typeof (RemotingShutdownEvent));
                Sys.EventStream.Subscribe(TestActor, typeof (RemotingErrorEvent));
                var terminationTask = Sys.Terminate();
                Assert.True(terminationTask.Wait(RemainingOrDefault), "Expected to terminate within 10 seconds, but didn't.");
            });
        }

        [Fact]
        public void RemotingTerminator_should_shutdown_promptly_with_some_associations()
        {
            var sys2 = ActorSystem.Create("System2", RemoteConfig);
            var sys2Address = RARP.For(sys2).Provider.DefaultAddress;

            // open an association
            var associated =
                Sys.ActorSelection(new RootActorPath(sys2Address)/"system"/"remote-watcher")
                    .ResolveOne(TimeSpan.FromSeconds(4))
                    .Result;

            Within(TimeSpan.FromSeconds(10), () =>
            {
                Sys.EventStream.Subscribe(TestActor, typeof(RemotingShutdownEvent));
                Sys.EventStream.Subscribe(TestActor, typeof(RemotingErrorEvent));
                var terminationTask = Sys.Terminate();
                ExpectMsg<RemotingShutdownEvent>();
                Assert.True(terminationTask.Wait(RemainingOrDefault), "Expected to terminate within 10 seconds, but didn't.");
            });

            // now terminate the second system
            Within(TimeSpan.FromSeconds(10), () =>
            {
                var terminationTask = sys2.Terminate();
                Assert.True(terminationTask.Wait(RemainingOrDefault), "Expected to terminate within 10 seconds, but didn't.");
            });
        }

        [Fact]
        public void RemotingTerminator_should_shutdown_properly_with_remotely_deployed_actor()
        {
            var sys2 = ActorSystem.Create("System2", RemoteConfig);
            var sys2Address = RARP.For(sys2).Provider.DefaultAddress;
            
            // open an association via remote deployment
            var associated =
                Sys.ActorOf(BlackHoleActor.Props.WithDeploy(Deploy.None.WithScope(new RemoteScope(sys2Address))),
                    "remote");
            Watch(associated);

            // verify that the association is open (don't terminate until handshake is finished)
            associated.Ask<ActorIdentity>(new Identify("foo"), RemainingOrDefault).Result.MessageId.ShouldBe("foo");
            
            // terminate the DEPLOYED system
            Within(TimeSpan.FromSeconds(20), () =>
            {
                var terminationTask = sys2.Terminate();
                Assert.True(terminationTask.Wait(RemainingOrDefault), "Expected to terminate within 10 seconds, but didn't.");

                ExpectTerminated(associated); // expect that the remote deployed actor is dead
            });

            // now terminate the DEPLOYER system
            Within(TimeSpan.FromSeconds(10), () =>
            {
                var terminationTask = Sys.Terminate();
                Assert.True(terminationTask.Wait(RemainingOrDefault), "Expected to terminate within 10 seconds, but didn't.");
            });
        }

        [Fact]
        public void RemotingTerminator_should_shutdown_properly_without_exception_logging_while_graceful_shutdown()
        {
            EventFilter.Exception<ShutDownAssociation>().Expect(0,
                () =>
                {
                    var sys2 = ActorSystem.Create("System2", RemoteConfig);
                    var sys2Address = RARP.For(sys2).Provider.DefaultAddress;

                    // open an association via remote deployment
                    var associated = Sys.ActorOf(BlackHoleActor.Props.WithDeploy(Deploy.None.WithScope(new RemoteScope(sys2Address))), "remote");

                    Watch(associated);

                    // verify that the association is open (don't terminate until handshake is finished)
                    associated.Ask<ActorIdentity>(new Identify("foo"), RemainingOrDefault).Result.MessageId.ShouldBe("foo");

                    // terminate the DEPLOYED system
                    Within(TimeSpan.FromSeconds(10), () =>
                    {
                        var terminationTask = sys2.Terminate();
                        Assert.True(terminationTask.Wait(RemainingOrDefault), "Expected to terminate within 10 seconds, but didn't.");

                        ExpectTerminated(associated); // expect that the remote deployed actor is dead
                    });

                    // now terminate the DEPLOYER system
                    Within(TimeSpan.FromSeconds(10), () =>
                    {
                        var terminationTask = Sys.Terminate();
                        Assert.True(terminationTask.Wait(RemainingOrDefault), "Expected to terminate within 10 seconds, but didn't.");
                    });
                });
        }
    }
}
