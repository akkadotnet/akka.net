using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Tests
{
    public class StashNullReferenceExceptionSpec : PersistenceSpec
    {
        public StashNullReferenceExceptionSpec(ITestOutputHelper output) 
            : base (Configuration("baseTest"), output)
        {
        }

        [Fact]
        public async Task ActorWithStashShouldNotThrowNullReferenceExceptionDuringShutdown()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.stdout-loglevel = DEBUG
            ")
                .WithFallback(Configuration("test"));

            var setup = ActorSystemSetup.Empty
                .WithSetup(BootstrapSetup.Create().WithConfig(config));

            var sys2 = ActorSystem.Create("nre-shutdown-system", setup);
            var actor = sys2.ActorOf(Props.Create<StashingPersistentActor>());

            Watch(actor);

            // Stashing should work as intended
            actor.Tell("stash");
            ExpectMsg("ok");
            actor.Tell("stash");
            ExpectMsg("ok");
            actor.Tell("stash");
            ExpectMsg("ok");
            actor.Tell("stash");
            ExpectMsg("ok");
            actor.Tell("unstash");
            ExpectMsg("unstashed-ok");

            ExpectMsg("unstashed-msg");
            ExpectMsg("unstashed-msg");
            ExpectMsg("unstashed-msg");
            ExpectMsg("unstashed-msg");

            ExpectNoMsg(TimeSpan.FromSeconds(1));

            // should be able to stash after UnstashAll
            actor.Tell("force-stash");
            ExpectMsg("ok");
            actor.Tell("force-stash");
            ExpectMsg("ok");
            actor.Tell("force-stash");
            ExpectMsg("ok");
            actor.Tell("force-stash");
            ExpectMsg("ok");

            // should not throw NRE with stash
            await sys2.Terminate();
        }

        [Fact]
        public async Task RestartedActorWithStashShouldNotThrowNullReferenceExceptionDuringShutdown()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.stdout-loglevel = DEBUG
            ")
                .WithFallback(Configuration("test"));

            var setup = ActorSystemSetup.Empty
                .WithSetup(BootstrapSetup.Create().WithConfig(config));

            var sys2 = ActorSystem.Create("nre-shutdown-system", setup);
            var actor = sys2.ActorOf(Props
                .Create<StashingPersistentActor>()
                .WithSupervisorStrategy(new OneForOneStrategy(e => Directive.Restart)) );

            Watch(actor);

            // Stashing should work as intended
            actor.Tell("stash");
            ExpectMsg("ok");
            actor.Tell("stash");
            ExpectMsg("ok");
            actor.Tell("stash");
            ExpectMsg("ok");
            actor.Tell("stash");
            ExpectMsg("ok");

            // Should be able to stash again after restart
            actor.Tell("die");

            // ok messages from replayed stash
            ExpectMsg("ok");
            ExpectMsg("ok");
            ExpectMsg("ok");
            ExpectMsg("ok");
            ExpectNoMsg(TimeSpan.FromSeconds(1));

            actor.Tell("stash");
            ExpectMsg("ok");

            // Should not throw NRE during shutdown
            actor.Tell("die");

            // ok messages from replayed stash
            ExpectMsg("ok");
            ExpectMsg("ok");
            ExpectMsg("ok");
            ExpectMsg("ok");
            ExpectMsg("ok");
            ExpectNoMsg(TimeSpan.FromSeconds(1));

            // actor should be available here
            // should not throw NRE
            await sys2.Terminate();
        }

        [Fact]
        public async Task ActorWithEmptyStashShouldNotThrowNullReferenceExceptionDuringShutdown()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.stdout-loglevel = DEBUG
            ")
                .WithFallback(Configuration("test"));

            var setup = ActorSystemSetup.Empty
                .WithSetup(BootstrapSetup.Create().WithConfig(config));

            var sys2 = ActorSystem.Create("nre-shutdown-system", setup);
            var actor = sys2.ActorOf<StashingPersistentActor>();

            // should not throw NRE
            actor.Tell("unstash");

            // should not throw NRE
            await sys2.Terminate();
        }

        [Fact]
        public async Task FreshActorWithEmptyStashShouldNotThrowNullReferenceExceptionDuringShutdown()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.stdout-loglevel = DEBUG
            ")
                .WithFallback(Configuration("test"));

            var setup = ActorSystemSetup.Empty
                .WithSetup(BootstrapSetup.Create().WithConfig(config));

            var sys2 = ActorSystem.Create("nre-shutdown-system", setup);
            var actor = sys2.ActorOf<StashingPersistentActor>();

            // should not throw NRE
            await sys2.Terminate();
        }

        class StashingPersistentActor : ReceivePersistentActor, IWithUnboundedStash
        {
            private bool _stashing = true;

            public StashingPersistentActor()
            {
                Recover<object>(msg =>
                {
                    // no-op
                    //Stash.Stash();
                });

                Command<object>(msg =>
                {
                    switch (msg)
                    {
                        case "stash" when _stashing:
                            Stash.Stash();
                            Sender.Tell("ok");
                            break;
                        case "stash":
                            Sender.Tell("unstashed-msg");
                            break;
                        case "force-stash":
                            Stash.Stash();
                            Sender.Tell("ok");
                            break;
                        case "unstash":
                            _stashing = false;
                            Stash.UnstashAll(e => true);
                            Sender.Tell("unstashed-ok");
                            break;
                        case "die":
                            throw new Exception("BOOM");
                    }
                    
                });
            }

            public override string PersistenceId => "p_id";
        }
    }
}
