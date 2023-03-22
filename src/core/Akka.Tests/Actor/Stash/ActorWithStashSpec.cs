//-----------------------------------------------------------------------
// <copyright file="ActorWithStashSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.TestKit.TestActors;
using Akka.Tests.TestUtils;
using Akka.Tests.Util;
using Xunit;

namespace Akka.Tests.Actor.Stash
{
    public class ActorWithStashSpec : AkkaSpec
    {
        private static StateObj _state;

        public ActorWithStashSpec() => _state = new StateObj(this);

        [Fact]
        public void An_actor_with_stash_must_stash_messages()
        {
            var stasher = ActorOf<StashingActor>("stashing-actor");
            stasher.Tell("bye");
            stasher.Tell("hello");
            _state.Finished.Await();
            _state.S.ShouldBe("bye");
        }

        [Fact]
        public void An_actor_with_stash_must_support_protocols()
        {
            var protoActor = ActorOf<ActorWithProtocol>();
            protoActor.Tell("open");
            protoActor.Tell("write");
            protoActor.Tell("open");
            protoActor.Tell("close");
            protoActor.Tell("write");
            protoActor.Tell("close");
            protoActor.Tell("done");
            _state.Finished.Await();
        }

        [Fact]
        public void An_actor_must_throw_an_exception_if_the_same_message_is_stashed_twice()
        {
            _state.ExpectedException = new TestLatch();
            var stasher = ActorOf<StashingTwiceActor>("stashing-actor");
            stasher.Tell("hello");
            stasher.Tell("hello");
            _state.ExpectedException.Ready(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task An_actor_Must_process_stashed_messages_after_restart()
        {
            var boss = ActorOf(() => new Supervisor(new OneForOneStrategy(2, TimeSpan.FromSeconds(1), e => Directive.Restart)));

            var restartLatch = CreateTestLatch();
            var hasMsgLatch = CreateTestLatch();

            var slaveProps = Props.Create(() => new SlaveActor(restartLatch, hasMsgLatch));
            var slave = await boss.Ask<IActorRef>(slaveProps).WithTimeout(TestKitSettings.DefaultTimeout);

            slave.Tell("hello");
            slave.Tell("crash");

            restartLatch.Ready(TimeSpan.FromSeconds(10));
            hasMsgLatch.Ready(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task An_actor_must_rereceive_unstashed_Terminated_messages()
        {
            ActorOf(Props.Create(() => new TerminatedMessageStashingActor(TestActor)), "terminated-message-stashing-actor");
            await ExpectMsgAsync("terminated");
            await ExpectMsgAsync("terminated");
        }

        private class StashingActor : TestReceiveActor, IWithStash
        {
            public StashingActor()
            {
                Receive("hello", m =>
                {
                    _state.S = "hello";
                    Stash.UnstashAll();
                    Become(Greeted);
                });
                ReceiveAny(m => Stash.Stash());
            }

            private void Greeted()
            {
                Receive("bye", _ =>
                {
                    _state.S = "bye";
                    _state.Finished.Await();
                });
                ReceiveAny(_ => { }); //Do nothing
            }

            public IStash Stash { get; set; }
        }

        private class StashingTwiceActor : TestReceiveActor, IWithStash
        {
            public StashingTwiceActor()
            {
                Receive("hello", m =>
                {
                    try
                    {
                        Stash.Stash();
                        Stash.Stash();
                    }
                    catch (IllegalActorStateException)
                    {
                        _state.ExpectedException.Open();
                    }
                });
                ReceiveAny(m => { }); // do nothing
            }

            public IStash Stash { get; set; }
        }

        private class ActorWithProtocol : TestReceiveActor, IWithStash
        {
            public ActorWithProtocol()
            {
                Receive("open", m =>
                {
                    Stash.UnstashAll();
                    Context.BecomeStacked(m =>
                    {
                        Receive("write", _ => { }); // do writing...
                        Receive("close", _ =>
                        {
                            Stash.UnstashAll();
                            Context.UnbecomeStacked();
                        });
                        ReceiveAny(_ => Stash.Stash());
                    });
                });
                Receive("done", m => _state.Finished.Await());
                ReceiveAny(_ => Stash.Stash());
            }

            public IStash Stash { get; set; }
        }

        private class SlaveActor : TestReceiveActor, IWithStash
        {
            private readonly TestLatch _restartLatch;

            public SlaveActor(TestLatch restartLatch, TestLatch hasMsgLatch)
            {
                _restartLatch = restartLatch;

                Receive("crash", _ => throw new Exception("Crashing..."));

                // when restartLatch is not yet open, stash all messages != "crash"                
                Receive<object>(_ => !restartLatch.IsOpen, m => Stash.Stash());

                // when restartLatch is open, must receive "hello"
                Receive("hello", _ => hasMsgLatch.Open());
            }

            protected override void PreRestart(Exception reason, object message)
            {
                if (!_restartLatch.IsOpen) _restartLatch.Open();

                base.PreRestart(reason, message);
            }
            public IStash Stash { get; set; }
        }

        private class TerminatedMessageStashingActor : TestReceiveActor, IWithUnboundedStash
        {
            public TerminatedMessageStashingActor(IActorRef probe)
            {
                var watchedActor = Context.Watch(Context.ActorOf<BlackHoleActor>("watched-actor"));
                var stashed = false;

                Context.Stop(watchedActor);

                Receive<Terminated>(w => w.ActorRef == watchedActor, w =>
                {
                    if (!stashed)
                    {
                        Stash.Stash();        //Stash the Terminated message
                        stashed = true;
                        Stash.UnstashAll();   //Unstash the Terminated message
                    }
                    probe.Tell("terminated");
                });
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

