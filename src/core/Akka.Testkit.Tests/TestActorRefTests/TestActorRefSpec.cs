using System;
using System.Threading;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Tests;
using Xunit;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class TestActorRefSpec : AkkaSpec
    {
        public static int Counter = 4;
        public static readonly Thread Thread = Thread.CurrentThread;
        public static Thread OtherThread = null;


        public TestActorRefSpec()
        {
            OtherThread = null;
        }

        protected override string GetConfig()
        {
            return @"test-dispatcher1.type=""" + typeof(TaskDispatcher).FullName+ @"""
                akka.test.default-timeout = 1.5 s
                akka.log-dead-letters-during-shutdown = true
                akka.actor.debug.autoreceive = true
                akka.actor.debug.lifecycle = true
                akka.actor.debug.event-stream = true
                akka.actor.debug.unhandled = true
                akka.log-dead-letters = true
                akka.loglevel = DEBUG
                akka.stdout-loglevel = DEBUG
            ";                
        }

        private void AssertThread()
        {
            Assert.True(OtherThread == null || OtherThread == Thread, "Thread");
        }

        [Fact]
        public void TestActorRef_must_support_nested_Actor_creation_when_used_with_TestActorRef()
        {
            var a = TestActorRef.Create<NestingActor>(System, () => new NestingActor(true));
            Assert.NotNull(a);
            var nested = a.Ask<ActorRef>("any", DefaultTimeout).Result;
            Assert.NotNull(nested);
            Assert.NotSame(a, nested);
        }

        [Fact]
        public void TestActorRef_must_support_nested_Actor_creation_when_used_with_ActorRef()
        {
            var a = TestActorRef.Create<NestingActor>(System, () => new NestingActor(false));
            Assert.NotNull(a);
            var nested = a.Ask<ActorRef>("any", DefaultTimeout).Result;
            Assert.NotNull(nested);
            Assert.NotSame(a, nested);
        }

        [Fact]
        public void TestActorRef_must_support_reply_via_sender()
        {
            var serverRef = TestActorRef.Create<ReplyActor>(System);
            var clientRef = TestActorRef.Create<SenderActor>(System, () => new SenderActor(serverRef));

            Counter = 4;
            clientRef.Tell("complex");
            clientRef.Tell("simple");
            clientRef.Tell("simple");
            clientRef.Tell("simple");
            Counter.ShouldBe(0);

            Counter = 4;
            clientRef.Tell("complex2");
            clientRef.Tell("simple");
            clientRef.Tell("simple");
            clientRef.Tell("simple");
            Counter.ShouldBe(0);

            AssertThread();
        }

        [Fact]
        public void TestActorRef_must_stop_when_sent_a_PoisonPill()
        {
            //TODO: Should have this surrounding all code EventFilter[ActorKilledException]() intercept {
            var a = TestActorRef.Create<WorkerActor>(System, "will-be-killed");
            var forwarder = System.ActorOf(Props.Create(() => new WatchAndForwardActor(a, testActor)), "forwarder");
            a.Tell(new PoisonPill());
            ExpectMsgPF<WrappedTerminated>(TimeSpan.FromSeconds(5), w => {Console.WriteLine("Received:"+w.Terminated.ActorRef); return w.Terminated.ActorRef == a; },
                string.Format("that the terminated actor was the one killed, i.e. {0}", a.Path));
            a.IsTerminated.ShouldBe(true);
            AssertThread();
        }

        [Fact]
        public void TestActorRef_must_restart_when_killed()
        {
            //TODO: Should have this surrounding all code EventFilter[ActorKilledException]() intercept {
            Counter = 2;
            var boss = TestActorRef.Create<BossActor>(sys);

            boss.Tell("sendKill");
            Assert.Equal(0, Counter);
            AssertThread();
        }

        [Fact]
        public void TestActorRef_must_support_futures()
        {
            var worker = TestActorRef.Create<WorkerActor>(sys);
            var task = worker.Ask("work");
            Assert.True(task.IsCompleted, "Task should be completed");
            if(!task.Wait(DefaultTimeout)) XAssert.Fail("Timed out");    //Using a timeout to stop the test if there is something wrong with the code
            Assert.Equal("workDone", task.Result);
        }

        [Fact]
        public void TestActorRef_must_allow_access_to_internals()
        {
            var actorRef = TestActorRef.Create<SaveStringActor>(sys);
            actorRef.Tell("Hejsan!");
            var actor = actorRef.UnderlyingActor;
            Assert.Equal("Hejsan!", actor.ReceivedString);
        }

        [Fact(Skip = "Context.ReceiveTimeout is not implemented. Test cannot run")]
        public void TestActorRef_must_set_ReceiveTimeout_to_None()
        {
            var a = TestActorRef.Create<WorkerActor>(sys);
            //TODO: When Context.ReceiveTimeout is implemented: Assert.Equal(((IInternalActor)a.UnderlyingActor).ActorContext.ReceiveTimeout, not sure what value to put here: null or Timeout.InfiniteTimeSpan);
        }

        [Fact]
        public void TestActorRef_must_set_CallingThreadDispatcher()
        {
            var a = TestActorRef.Create<WorkerActor>(sys);
            Assert.IsType<CallingThreadDispatcher>(a.Cell.Dispatcher);
        }

        [Fact]
        public void TestActorRef_must_allow_override_of_dispatcher()
        {
            var a = TestActorRef.Create<WorkerActor>(sys, Props.Create<WorkerActor>().WithDispatcher("test-dispatcher1"));
            Assert.IsType<TaskDispatcher>(a.Cell.Dispatcher);
        }

        [Fact]
        public void TestActorRef_must_proxy_receive_for_the_underlying_actor_without_sender()
        {
            var a = TestActorRef.Create<WorkerActor>(sys);
            a.Receive("work");
            Assert.True(a.IsTerminated);
        }

        [Fact]
        public void TestActorRef_must_proxy_receive_for_the_underlying_actor_with_sender()
        {
            var a = TestActorRef.Create<WorkerActor>(sys);
            a.Receive("work", testActor);
            Assert.True(a.IsTerminated);
            expectMsg("workDone");
        }

        private class SaveStringActor : TActorBase
        {
            public string ReceivedString { get; set; }

            protected override bool ReceiveMessage(object message)
            {
                ReceivedString = message as string;
                return true;
            }
        }
    }
}