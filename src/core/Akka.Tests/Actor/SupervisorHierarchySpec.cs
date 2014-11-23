using System;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class SupervisorHierarchySpec : AkkaSpec
    {
        private class Resumer : TestReceiveActor
        {
            public Resumer()
            {
                Receive<string>(s => s.StartsWith("spawn:"), s => Sender.Tell(Context.ActorOf<Resumer>(s.Substring(6))));
                Receive("spawn", _ => Sender.Tell(Context.ActorOf<Resumer>()));
                Receive("fail", _ => { throw new Exception("expected"); });
                Receive("ping", _ => Sender.Tell("pong"));
            }

            protected override SupervisorStrategy SupervisorStrategy()
            {
                return new OneForOneStrategy(_ => Directive.Resume);
            }
        }

        public SupervisorHierarchySpec()
            : base(FullDebugConfig)
        {

        }
        [Fact]
        public void Suspend_children_while_failing()
        {
            var latch = CreateTestLatch();
            var slowResumer = ActorOf(c =>
            {
                c.Strategy = new OneForOneStrategy(e => { latch.Ready(Dilated(TimeSpan.FromSeconds(4))); return Directive.Resume; });
                c.Receive<string>(s => s.StartsWith("spawn:"), (s, ctx) => ctx.Sender.Tell(ctx.ActorOf<Resumer>(s.Substring(6))));
                c.Receive("spawn", (s, ctx) => ctx.Sender.Tell(ctx.ActorOf<Resumer>()));
            }, "slowResumer");

            //Build this hiearchy:
            //  slowResumer
            //      |
            //     boss
            //      |
            //    middle
            //      |
            //    worker
            slowResumer.Tell("spawn:boss");
            var boss = ExpectMsg<ActorRef>();
            boss.Tell("spawn:middle");
            var middle = ExpectMsg<ActorRef>();
            middle.Tell("spawn:worker");
            var worker = ExpectMsg<ActorRef>();

            //Check everything is in place by sending ping to worker and expect it to respond with pong
            worker.Tell("ping");
            ExpectMsg("pong");
            EventFilter.Warning("expected").ExpectOne(() => //expected exception is thrown by the boss when it crashes
            {
                //Let boss crash, this means any child under boss should be suspended, so we wait for worker to become suspended.                
                boss.Tell("fail");
                AwaitCondition(() => ((LocalActorRef)worker).Cell.Mailbox.IsSuspended);

                //At this time slowresumer is currently handling the failure, in supervisestrategy, waiting for latch to be opened
                //We verify that no message is handled by worker, by sending it a ping
                //Normally it would respond with a pong, but since it's suspended nothing will happen.
                worker.Tell("ping");
                ExpectNoMsg(TimeSpan.FromSeconds(1));

                //By counting down the latch slowResumer will continue in the supervisorstrategy and will return Resume.
                latch.CountDown();
            });

            //Check that all children, and especially worker is resumed. It should receive the ping and respond with a pong
            ExpectMsg("pong");
        }
    }
}