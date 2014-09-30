using System;
using System.Threading;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Tests.TestUtils;
using Xunit;

namespace Akka.Tests.Actor.Stash
{
    public class ActorWithStashSpec : AkkaSpec
    {
        private static StateObj _state;
        public ActorWithStashSpec()
        {
            _state = new StateObj(this);
        }

        [Fact]
        public void An_actor_with_unbounded_stash_must_have_a_stash_assigned_to_it()
        {
            var actor = ActorOfAsTestActorRef<UnboundedStashActor>();
            Assert.NotNull(actor.UnderlyingActor.Stash);
            Assert.IsAssignableFrom<UnboundedStashImpl>(actor.UnderlyingActor.Stash);
        }

        [Fact]
        public void An_actor_with_Stash_Must_stash_and_unstash_messages()
        {
            var stasher = ActorOf<StashingActor>("stashing-actor");
            stasher.Tell("bye");    //will be stashed
            stasher.Tell("hello");  //forces UnstashAll
            _state.Finished.Await();
            _state.S.ShouldBe("bye");
        }

        [Fact]
        public void An_actor_Must_throw_an_exception_if_the_same_message_is_stashed_twice()
        {
            _state.ExpectedException=new TestLatch(Sys);
            var stasher = ActorOf<StashingTwiceActor>("stashing-actor");
            stasher.Tell("hello");
            _state.ExpectedException.Ready(TimeSpan.FromSeconds(3));
        }

        [Fact]
        public void An_actor_Must_process_stashed_messages_after_restart()
        {
            SupervisorStrategy strategy=new OneForOneStrategy(2,TimeSpan.FromSeconds(1),e=>Directive.Restart);
            var boss = ActorOf(() => new Supervisor(strategy));
            var restartLatch = CreateTestLatch();
            var hasMsgLatch = CreateTestLatch();

           throw new Exception("Incomplete");
        }

        private class UnboundedStashActor : BlackHoleActor, WithUnboundedStash
        {
            public IStash Stash { get; set; }
        }

        private class StashingActor : ReceiveActor, WithUnboundedStash
        {
            public StashingActor()
            {
                Receive<string>(m => m == "hello", m =>
                {
                    _state.S = "hello";
                    Stash.UnstashAll();
                    Become(Greeted);
                });
                ReceiveAny(m => Stash.Stash());
            }

            private void Greeted()
            {
                Receive<string>(s => s == "bye", _ =>
                {
                    _state.S = "bye";
                    _state.Finished.Await();
                });
                ReceiveAny(_ => { }); //Do nothing
            }

            public IStash Stash { get; set; }
        }

        private class StashingTwiceActor : ReceiveActor, WithUnboundedStash
        {
            public StashingTwiceActor()
            {
                Receive<string>(m => m == "hello", m =>
                {
                    Stash.Stash();
                    try
                    {
                        Stash.Stash();
                    }
                    catch(IllegalActorStateException e)
                    {
                        _state.ExpectedException.Open();
                    }
                });
                ReceiveAny(m => { });
            }

            private void Greeted()
            {
                Receive<string>(s => s == "bye", _ =>
                {
                    _state.S = "bye";
                    _state.Finished.Await();
                });
                ReceiveAny(_ => { }); //Do nothing
            }

            public IStash Stash { get; set; }
        }


        private class StateObj
        {
            public StateObj(TestKitBase testKit)
            {
                S = "";
                Finished = testKit.CreateTestBarrier(2);
            }
            public string S;
            public TestBarrier Finished;
            public TestLatch ExpectedException;
        }
    }


}