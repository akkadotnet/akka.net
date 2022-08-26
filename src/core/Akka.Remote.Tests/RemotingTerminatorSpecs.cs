//-----------------------------------------------------------------------
// <copyright file="RemotingTerminatorSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

        private ActorSystem _sys2;
        
        public RemotingTerminatorSpecs(ITestOutputHelper output) : base(RemoteConfig, output) { }

        protected override async Task AfterAllAsync()
        {
            await base.AfterAllAsync();
            if (_sys2 != null)
                await ShutdownAsync(_sys2);
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
            Watch(associated);

            // verify that the association is open (don't terminate until handshake is finished)
            associated.Ask<ActorIdentity>(new Identify("foo"), RemainingOrDefault).Result.MessageId.ShouldBe("foo");
            
            
            // terminate the DEPLOYED system
            Assert.True(await _sys2.Terminate().AwaitWithTimeout(10.Seconds()), "Expected to terminate within 10 seconds, but didn't.");
            await ExpectTerminatedAsync(associated); // expect that the remote deployed actor is dead

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

                    Watch(associated);

                    // verify that the association is open (don't terminate until handshake is finished)
                    (await associated.Ask<ActorIdentity>(new Identify("foo"), RemainingOrDefault)).MessageId.ShouldBe("foo");

                    // terminate the DEPLOYED system
                    Assert.True(await _sys2.Terminate().AwaitWithTimeout(10.Seconds()), "Expected to terminate within 10 seconds, but didn't.");
                    await ExpectTerminatedAsync(associated); // expect that the remote deployed actor is dead
                    
                    // now terminate the DEPLOYER system
                    Assert.True(await Sys.Terminate().AwaitWithTimeout(10.Seconds()), "Expected to terminate within 10 seconds, but didn't.");
                });
        }
    }
}
