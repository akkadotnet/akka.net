using System;

using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;

using Xunit;

namespace Akka.Tests.Routing
{
    public class RouterSupervisorSpec : AkkaSpec
    {
        #region Killable actor

        class KillableActor : ReceiveActor
        {
            private readonly IActorRef TestActor;

            public KillableActor(IActorRef testActor)
            {
                this.TestActor = testActor;
                this.Receive<string>(s => s == "go away", s =>
                    {
                        throw new ArgumentException("Goodbye then!");
                    });
            }
        }
        
        #endregion

        #region Tests

        [Fact]
        public void Routers_must_use_provided_supervisor_strategy()
        {
            var router = this.Sys.ActorOf(Props.Create(() => new KillableActor(this.TestActor)).WithRouter(new RoundRobinPool(1, null, new OneForOneStrategy(exception =>
                    {
                        this.TestActor.Tell("supervised");
                        return Directive.Stop;
                    }), null)), "router1");
            
            router.Tell("go away");
            
            this.ExpectMsg("supervised", TimeSpan.FromSeconds(2));
        }
        
        #endregion
    }
}