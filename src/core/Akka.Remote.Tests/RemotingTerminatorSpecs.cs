using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
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
            akka.remote.helios.tcp {
                hostname = 127.0.0.1
                port = 0
            }
        ");

        public RemotingTerminatorSpecs() : base(RemoteConfig) { }

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
                Assert.True(terminationTask.Wait(RemainingOrDefault), "Expected to terminate within 10 seconds, but didn't.");
            });

            // now terminate the second system
            Within(TimeSpan.FromSeconds(10), () =>
            {
                var terminationTask = sys2.Terminate();
                Assert.True(terminationTask.Wait(RemainingOrDefault), "Expected to terminate within 10 seconds, but didn't.");
            });
        }
    }
}
