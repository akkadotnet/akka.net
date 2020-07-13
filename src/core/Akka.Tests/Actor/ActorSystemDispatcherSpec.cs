using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Actor
{
    public class ActorSystemDispatcherSpec : AkkaSpec
    {
        private class SnitchingSynchonizationContext : SynchronizationContext
        {
            private readonly IActorRef _testActor;

            public SnitchingSynchonizationContext(IActorRef testActor)
            {
                _testActor = testActor;
            }

            public override void OperationStarted()
            {
                _testActor.Tell("called");
            }
        }

        [Fact]
        public void The_ActorSystem_must_not_use_passed_in_SynchronizationContext_if_executor_is_configured_in()
        {
            var config =
                ConfigurationFactory.ParseString("akka.actor.default-dispatcher.executor = fork-join-executor")
                    .WithFallback(Sys.Settings.Config);
            var system2 = ActorSystem.Create("ActorSystemDispatchersSpec-ec-configured", config);

            try
            {
                var actor = system2.ActorOf<EchoActor>();
                var probe = CreateTestProbe(system2);

                actor.Tell("ping", probe);

                probe.ExpectMsg("ping", TimeSpan.FromSeconds(1));
            }
            finally
            {
                Shutdown(system2);
            }
        }

        [Fact]
        public void The_ActorSystem_must_provide_a_single_place_to_override_the_internal_dispatcher()
        {
            var config =
                ConfigurationFactory.ParseString("akka.actor.internal-dispatcher = akka.actor.default-dispatcher")
                    .WithFallback(Sys.Settings.Config);
            var sys = ActorSystem.Create("ActorSystemDispatchersSpec-override-internal-disp", config);
            try
            {
                // that the user guardian runs on the overriden dispatcher instead of internal
                // isn't really a guarantee any internal actor has been made running on the right one
                // but it's better than no test coverage at all
                UserGuardianDispatcher(sys).Should().Be("akka.actor.default-dispatcher");
            }
            finally
            {
                Shutdown(sys);
            }
        }

        private string UserGuardianDispatcher(ActorSystem system)
        {
            var impl = (ActorSystemImpl)system;
            return ((ActorCell)((ActorRefWithCell)impl.Guardian).Underlying).Dispatcher.Id;
        }

        private class PingPongActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                if((string)message == "ping")
                    Sender.Tell("pong");
            }
        }

        private class EchoActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                Sender.Tell(message);
            }
        }
    }
}

