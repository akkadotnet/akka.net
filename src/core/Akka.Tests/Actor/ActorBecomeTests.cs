//-----------------------------------------------------------------------
// <copyright file="ActorBecomeTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    
    public class ActorBecomeTests : AkkaSpec
    {

        [Fact]
        public void When_calling_become_Then_the_new_handler_is_used()
        {

            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<BecomeActor>("become");

            //When
            actor.Tell("DEFAULTBECOME", TestActor);
            actor.Tell("hello", TestActor);

            //Then
            ExpectMsg("2:hello");
        }


        [Fact]
        public void Given_actor_that_has_called_default_Become_twice_When_calling_unbecome_Then_the_default_handler_is_used_and_not_the_last_handler()
        {
            //Calling Become() does not persist the current handler, it just overwrites it, so when we call Unbecome(),
            //no matter how many times, there is no persisted handler to revert to, so we'll end up with the default one

            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<Become2Actor>("become"); 
            //Now OnReceive is used
            actor.Tell("DEFAULTBECOME", TestActor);
            //Now OnReceive2 is used
            actor.Tell("DEFAULTBECOME", TestActor);
            //Now OnReceive3 is used

            //When
            actor.Tell("UNBECOME", TestActor);
            //Since we used the default Become(receive) above, i.e. Become(receive, discardOld:true)
            //the OnReceive2 was overwritten, so the actor will revert to the default one, ie OnReceive
            actor.Tell("hello", TestActor);

            //Then
            ExpectMsg("1:hello");
        }


        [Fact]
        public void Given_actor_that_has_called_default_Become_without_overwriting_previous_handler_When_calling_unbecome_Then_the_previous_handler_is_used()
        {
            //Calling Become() does not persist the current handler, it just overwrites it, so when we call Unbecome(),
            //no matter how many times, there is no persisted handler to revert to, so we'll end up with the default one

            //Given
            var system = ActorSystem.Create("test");
            var actor = system.ActorOf<Become2Actor>("become");
            //Now OnReceive is used
            actor.Tell("BECOMESTACKED", TestActor);
            //Now OnReceive2 is used
            actor.Tell("BECOMESTACKED", TestActor);
            //Now OnReceive3 is used, and OnReceive2 was persisted

            //When
            actor.Tell("UNBECOME", TestActor);
            //Since we used Become(receive, discardOld:true) the actor will revert to OnReceive2
            actor.Tell("hello", TestActor);

            //Then
            ExpectMsg("2:hello");
        }

        [Fact]
        public void Given_actor_that_calls_become_in_the_become_handler_only_first_become_receive_set_is_used() {
            var system = ActorSystem.Create("test");

            //Given, this actor calls become(A) inside A() it calls Become(B);
            var actor = system.ActorOf<Become3Actor>("become");

            //only the handler set of A() should be active

            actor.Tell("hi", TestActor);
            actor.Tell(true, TestActor);
            
            //which means this message should never be handled, because only B() has a receive for this.
            actor.Tell(2, TestActor);

            ExpectMsg("A says: hi");
            ExpectMsg("A says: True");
            //we dont expect any further messages
            this.ExpectNoMsg();
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
                        BecomeStacked(OnReceive2);
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
                        UnbecomeStacked();
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
                        BecomeStacked(OnReceive2);
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
                        BecomeStacked(OnReceive3);
                        break;
                    case "UNBECOME":
                        UnbecomeStacked();
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
                        UnbecomeStacked();
                        break;
                    default:
                        Sender.Tell("3:" + s, Self);
                        break;
                }
            }
        }

        private class Become3Actor : ReceiveActor
        {
            public Become3Actor()
            {
                Become(A);
            }

            public void A()
            {
                Receive<string>(s => {
                    Sender.Tell("A says: " + s);
                });
                Receive<bool>(s => {
                    Sender.Tell("A says: " + s);
                });
                //calling become before or after setting up the receive handlers makes no difference.
                Become(B);
            }

            public void B()
            {
                Receive<string>(s => {
                    Sender.Tell("B says: " + s);
                });
                Receive<bool>(s => {
                    Sender.Tell("B says: " + s);
                });
                Receive<int>(s => {
                    Sender.Tell("B says: " + s);
                });
            }
        }
    }
}

