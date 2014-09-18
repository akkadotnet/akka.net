using System;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.TestKit.Internal;
using Akka.TestKit.Internals;
using Akka.Util;
using Xunit;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class TestActorRefSpec : AkkaSpec
    {
        public static int Counter = 4;
        public static readonly Thread Thread = Thread.CurrentThread;
        public static Thread OtherThread = null;


        public TestActorRefSpec()
            : base(GetConfig())
        {
            OtherThread = null;
        }
        private TimeSpan DefaultTimeout { get { return Dilated(TestKitSettings.DefaultTimeout); } }

        private static Config GetConfig()
        {
            return (@"test-dispatcher1.type=""" + typeof(TaskDispatcher).FullName);
            //return (@"test-dispatcher1.type=""" + typeof(TaskDispatcher).FullName) + FullDebugConfig;
        }

        private void AssertThread()
        {
            Assert.True(OtherThread == null || OtherThread == Thread, "Thread");
        }

        [Fact]
        public void TestActorRef_name_must_start_with_double_dollar_sign()
        {
            //Looking at the scala code, this might not be obvious that the name starts with $$
            //object TestActorRef (TestActorRef.scala) contain this code: 
            //    private[testkit] def randomName: String = {
            //      val l = number.getAndIncrement()
            //      "$" + akka.util.Helpers.base64(l)
            //    }
            //So it adds one $. The second is added by akka.util.Helpers.base64(l) which by default 
            //creates a StringBuilder and adds adds $. Hence, 2 $$
            var testActorRef = TestActorRef.Create<ReplyActor>(Sys);

            Assert.Equal(testActorRef.Path.Name.Substring(0, 2), "$$");
        }

        [Fact]
        public void TestActorRef_must_support_nested_Actor_creation_when_used_with_TestActorRef()
        {
            var a = TestActorRef.Create<NestingActor>(Sys, () => new NestingActor(true));
            Assert.NotNull(a);
            var nested = a.Ask<ActorRef>("any", DefaultTimeout).Result;
            Assert.NotNull(nested);
            Assert.NotSame(a, nested);
        }

        [Fact]
        public void TestActorRef_must_support_nested_Actor_creation_when_used_with_ActorRef()
        {
            var a = TestActorRef.Create<NestingActor>(Sys, () => new NestingActor(false));
            Assert.NotNull(a);
            var nested = a.Ask<ActorRef>("any", DefaultTimeout).Result;
            Assert.NotNull(nested);
            Assert.NotSame(a, nested);
        }

        [Fact]
        public void TestActorRef_must_support_reply_via_sender()
        {
            var serverRef = TestActorRef.Create<ReplyActor>(Sys);
            var clientRef = TestActorRef.Create<SenderActor>(Sys, () => new SenderActor(serverRef));

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
            var a = TestActorRef.Create<WorkerActor>(Sys, "will-be-killed");
            var forwarder = Sys.ActorOf(Props.Create(() => new WatchAndForwardActor(a, TestActor)), "forwarder");
            a.Tell(PoisonPill.Instance);
            ExpectMsg<WrappedTerminated>(TimeSpan.FromSeconds(10), string.Format("that the terminated actor was the one killed, i.e. {0}", a.Path), w => w.Terminated.ActorRef == a);
            var actorRef = (InternalTestActorRef)a.Ref;
            actorRef.IsTerminated.ShouldBe(true);
            AssertThread();
        }

        [Fact]
        public void TestActorRef_must_restart_when_killed()
        {
            //TODO: Should have this surrounding all code EventFilter[ActorKilledException]() intercept {
            Counter = 2;
            var boss = TestActorRef.Create<BossActor>(Sys);

            boss.Tell("sendKill");
            Assert.Equal(0, Counter);
            AssertThread();
        }

        [Fact]
        public void TestActorRef_must_support_futures()
        {
            var worker = TestActorRef.Create<WorkerActor>(Sys);
            var task = worker.Ask("work");
            Assert.True(task.IsCompleted, "Task should be completed");
            if(!task.Wait(DefaultTimeout)) XAssert.Fail("Timed out");    //Using a timeout to stop the test if there is something wrong with the code
            Assert.Equal("workDone", task.Result);
        }

        [Fact]
        public void TestActorRef_must_allow_access_to_internals()
        {
            var actorRef = TestActorRef.Create<SaveStringActor>(Sys);
            actorRef.Tell("Hejsan!");
            var actor = actorRef.UnderlyingActor;
            Assert.Equal("Hejsan!", actor.ReceivedString);
        }

        [Fact(Skip = "Context.ReceiveTimeout is not implemented. Test cannot run")]
        public void TestActorRef_must_set_ReceiveTimeout_to_None()
        {
            var a = TestActorRef.Create<WorkerActor>(Sys);
            //TODO: When Context.ReceiveTimeout is implemented: Assert.Equal(((IInternalActor)a.UnderlyingActor).ActorContext.ReceiveTimeout, not sure what value to put here: null or Timeout.InfiniteTimeSpan);
        }

        [Fact]
        public void TestActorRef_must_set_CallingThreadDispatcher()
        {
            var a = TestActorRef.Create<WorkerActor>(Sys);
            var actorRef = (InternalTestActorRef)a.Ref;
            Assert.IsType<CallingThreadDispatcher>(actorRef.Cell.Dispatcher);
        }

        [Fact]
        public void TestActorRef_must_allow_override_of_dispatcher()
        {
            var a = TestActorRef.Create<WorkerActor>(Sys, Props.Create<WorkerActor>().WithDispatcher("test-dispatcher1"));
            var actorRef = (InternalTestActorRef)a.Ref;
            Assert.IsType<TaskDispatcher>(actorRef.Cell.Dispatcher);
        }

        [Fact]
        public void TestActorRef_must_proxy_receive_for_the_underlying_actor_without_sender()
        {
            var a = TestActorRef.Create<WorkerActor>(Sys);
            a.Receive("work");
            var actorRef = (InternalTestActorRef)a.Ref;
            Assert.True(actorRef.IsTerminated);
        }

        [Fact]
        public void TestActorRef_must_proxy_receive_for_the_underlying_actor_with_sender()
        {
            var a = TestActorRef.Create<WorkerActor>(Sys);
            a.Receive("work", TestActor);
            var actorRef = (InternalTestActorRef)a.Ref;
            Assert.True(actorRef.IsTerminated);
            ExpectMsg("workDone");
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