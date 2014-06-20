using System;
using Akka.Actor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Tests.Actor
{
    [TestClass]
    public class ActorBecomeTests : AkkaSpec
    {
        private static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(2);

        [TestMethod]
        public void When_calling_become_Then_the_new_handler_is_used()
        {
            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<BecomeActor>("become");

            //When
            actor.Tell("DEFAULTBECOME", testActor);
            actor.Tell("hello", testActor);

            //Then
            expectMsg("2:hello", _defaultTimeout);
        }


        [TestMethod]
        public void Given_actor_that_has_called_default_Become_twice_When_calling_unbecome_Then_the_default_handler_is_used_and_not_the_last_handler()
        {
            //Calling Become() does not persist the current handler, it just overwrites it, so when we call Unbecome(),
            //no matter how many times, there is no persisted handler to revert to, so we'll end up with the default one

            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<Become2Actor>("become"); 
            //Now OnReceive is used
            actor.Tell("DEFAULTBECOME", testActor);
            //Now OnReceive2 is used
            actor.Tell("DEFAULTBECOME", testActor);
            //Now OnReceive3 is used

            //When
            actor.Tell("UNBECOME", testActor);
            //Since we used the default Become(receive) above, i.e. Become(receive, discardOld:true)
            //the OnReceive2 was overwritten, so the actor will revert to the default one, ie OnReceive
            actor.Tell("hello", testActor);

            //Then
            expectMsg("1:hello", _defaultTimeout);
        }


        [TestMethod]
        public void Given_actor_that_has_called_default_Become_without_overwriting_previous_handler_When_calling_unbecome_Then_the_previous_handler_is_used()
        {
            //Calling Become() does not persist the current handler, it just overwrites it, so when we call Unbecome(),
            //no matter how many times, there is no persisted handler to revert to, so we'll end up with the default one

            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<Become2Actor>("become");
            //Now OnReceive is used
            actor.Tell("BECOMESTACKED", testActor);
            //Now OnReceive2 is used
            actor.Tell("BECOMESTACKED", testActor);
            //Now OnReceive3 is used, and OnReceive2 was persisted

            //When
            actor.Tell("UNBECOME", testActor);
            //Since we used Become(receive, discardOld:true) the actor will revert to OnReceive2
            actor.Tell("hello", testActor);

            //Then
            expectMsg("2:hello", _defaultTimeout);
        }

        private class BecomeActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                var s = (string)message;
                switch(s)
                {
                    case "DEFAULTBECOME":
                        Become(OnReceive2);
                        break;
                    case "BECOMESTACKED":
                        Become(OnReceive2,discardOld: false);
                        break;
                    default:
                        Sender.Tell("1:" + s, Self);
                        break;
                }                
            }

            private void OnReceive2(object message)
            {
                var s = (string)message;
                switch(s)
                {
                    case "UNBECOME":
                        Unbecome();
                        break;
                    default:
                        Sender.Tell("2:" + s, Self);
                        break;
                }
            }
        }

        private class Become2Actor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                var s = (string)message;
                switch(s)
                {
                    case "DEFAULTBECOME":
                        Become(OnReceive2);
                        break;
                    case "BECOMESTACKED":
                        Become(OnReceive2, discardOld: false);
                        break;
                    default:
                        Sender.Tell("1:" + s, Self);
                        break;
                }
            }

            private void OnReceive2(object message)
            {
                var s = (string)message;
                switch(s)
                {
                    case "DEFAULTBECOME":
                        Become(OnReceive3);
                        break;
                    case "BECOMESTACKED":
                        Become(OnReceive3, discardOld: false);
                        break;
                    case "UNBECOME":
                        Unbecome();
                        break;
                    default:
                        Sender.Tell("2:" + s, Self);
                        break;
                }
            }

            private void OnReceive3(object message)
            {
                var s = (string)message;
                switch(s)
                {
                    case "UNBECOME":
                        Unbecome();
                        break;
                    default:
                        Sender.Tell("3:" + s, Self);
                        break;
                }
            }
        }
    }
}