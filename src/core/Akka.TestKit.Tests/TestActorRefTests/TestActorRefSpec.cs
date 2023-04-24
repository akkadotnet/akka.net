//-----------------------------------------------------------------------
// <copyright file="TestActorRefSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.TestKit.Internal;
using Akka.Util;
using Akka.Util.Internal;
using Xunit;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class TestActorRefSpec : AkkaSpec
    {
        private readonly AtomicCounter _counter = new AtomicCounter(4);
        private readonly Thread _thread = Thread.CurrentThread;
        private readonly AtomicReference<Thread> _otherThread = new AtomicReference<Thread>(null);

        public TestActorRefSpec()
            : base(GetConfig())
        {
        }
        private TimeSpan DefaultTimeout { get { return Dilated(TestKitSettings.DefaultTimeout); } }

        private static Config GetConfig()
        {
            return (@"test-dispatcher1.type=""" + typeof(PinnedDispatcherConfigurator).FullName) + @"""";
            //return (@"test-dispatcher1.type=""" + typeof(TaskDispatcher).FullName) + FullDebugConfig;
        }

        private void AssertThread()
        {
            Assert.True(_otherThread.Value == null || _otherThread.Value == _thread, "Thread");
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
            var testActorRef = new TestActorRef<ReplyActor>(Sys, Props.Create<ReplyActor>());

            Assert.Equal("$$", testActorRef.Path.Name.Substring(0, 2));
        }

        [Fact]
        public void TestActorRef_must_support_nested_Actor_creation_when_used_with_TestActorRef()
        {
            var a = new TestActorRef<NestingActor>(Sys, Props.Create(() => new NestingActor(true)));
            Assert.NotNull(a);
            var nested = a.Ask<IActorRef>("any", DefaultTimeout).Result;
            Assert.NotNull(nested);
            Assert.NotSame(a, nested);
        }

        [Fact]
        public void TestActorRef_must_support_nested_Actor_creation_when_used_with_ActorRef()
        {
            var a = new TestActorRef<NestingActor>(Sys, Props.Create(() => new NestingActor(false)));
            Assert.NotNull(a);
            var nested = a.Ask<IActorRef>("any", DefaultTimeout).Result;
            Assert.NotNull(nested);
            Assert.NotSame(a, nested);
        }

        [Fact]
        public void TestActorRef_must_support_reply_via_sender()
        {
            var serverRef = new TestActorRef<ReplyActor>(Sys, Props.Create(() => 
                new ReplyActor(_thread, _otherThread)));
            var clientRef = new TestActorRef<SenderActor>(Sys, Props.Create(() => 
                new SenderActor(serverRef, _counter, _thread, _otherThread)));

            _counter.GetAndSet(4);
            clientRef.Tell("complex");
            clientRef.Tell("simple");
            clientRef.Tell("simple");
            clientRef.Tell("simple");
            _counter.Current.ShouldBe(0);

            _counter.GetAndSet(4);
            clientRef.Tell("complex2");
            clientRef.Tell("simple");
            clientRef.Tell("simple");
            clientRef.Tell("simple");
            _counter.Current.ShouldBe(0);

            AssertThread();
        }

        [Fact]
        public void TestActorRef_must_stop_when_sent_a_PoisonPill()
        {
            //TODO: Should have this surrounding all code EventFilter[ActorKilledException]() intercept {            
            var a = new TestActorRef<WorkerActor>(Sys, Props.Create(() => new WorkerActor(_thread, _otherThread)), null, "will-be-killed");
            Sys.ActorOf(Props.Create(() => new WatchAndForwardActor(a, TestActor)), "forwarder");
            a.Tell(PoisonPill.Instance);
            ExpectMsg<WrappedTerminated>(w => w.Terminated.ActorRef == a, TimeSpan.FromSeconds(10), string.Format("that the terminated actor was the one killed, i.e. {0}", a.Path));
            var actorRef = (InternalTestActorRef)a.Ref;
            actorRef.IsTerminated.ShouldBe(true);
            AssertThread();
        }

        [Fact]
        public void TestActorRef_must_restart_when_killed()
        {
            //TODO: Should have this surrounding all code EventFilter[ActorKilledException]() intercept {
            _counter.GetAndSet(2);
            var boss = new TestActorRef<BossActor>(Sys, Props.Create(() => new BossActor(_counter, _thread, _otherThread)));

            boss.Tell("sendKill");
            Assert.Equal(0, _counter.Current);
            AssertThread();
        }

        [Fact]
        public void TestActorRef_must_support_futures()
        {
            var worker = new TestActorRef<WorkerActor>(Sys, Props.Create(() => new WorkerActor(_thread, _otherThread)));
            var task = worker.Ask("work");
            Assert.True(task.IsCompleted, "Task should be completed");
            if(!task.Wait(DefaultTimeout)) XAssert.Fail("Timed out");    //Using a timeout to stop the test if there is something wrong with the code
            Assert.Equal("workDone", task.Result);
        }

        [Fact]
        public void TestActorRef_must_allow_access_to_internals()
        {
            var actorRef = new TestActorRef<SaveStringActor>(Sys, Props.Create(() => new SaveStringActor(_thread, _otherThread)));
            actorRef.Tell("Hejsan!");
            var actor = actorRef.UnderlyingActor;
            Assert.Equal("Hejsan!", actor.ReceivedString);
        }

        [Fact]
        public void TestActorRef_must_set_ReceiveTimeout_to_None()
        {
            var a = new TestActorRef<WorkerActor>(Sys, Props.Create(() => new WorkerActor(_thread, _otherThread)));
            ((IInternalActor)a.UnderlyingActor).ActorContext.ReceiveTimeout.ShouldBe(null);
        }

        [Fact]
        public void TestActorRef_must_set_CallingThreadDispatcher()
        {
            var a = new TestActorRef<WorkerActor>(Sys, Props.Create(() => new WorkerActor(_thread, _otherThread)));
            var actorRef = (InternalTestActorRef)a.Ref;
            Assert.IsType<CallingThreadDispatcher>(actorRef.Cell.Dispatcher);
        }

        [Fact]
        public void TestActorRef_must_allow_override_of_dispatcher()
        {
            var a = new TestActorRef<WorkerActor>(Sys, Props.Create(() => new WorkerActor(_thread, _otherThread)).WithDispatcher("test-dispatcher1"));
            var actorRef = (InternalTestActorRef)a.Ref;
            Assert.IsType<PinnedDispatcher>(actorRef.Cell.Dispatcher);
        }

        [Fact]
        public void TestActorRef_must_proxy_receive_for_the_underlying_actor_without_sender()
        {
            var a = new TestActorRef<WorkerActor>(Sys, Props.Create(() => new WorkerActor(_thread, _otherThread)));
            a.Receive("work");
            var actorRef = (InternalTestActorRef)a.Ref;
            Assert.True(actorRef.IsTerminated);
        }

        [Fact]
        public void TestActorRef_must_proxy_receive_for_the_underlying_actor_with_sender()
        {
            var a = new TestActorRef<WorkerActor>(Sys, Props.Create(() => new WorkerActor(_thread, _otherThread)));
            a.Receive("work", TestActor);   //This will stop the actor
            var actorRef = (InternalTestActorRef)a.Ref;
            Assert.True(actorRef.IsTerminated);
            ExpectMsg("workDone");
        }

        [Fact]
        public void TestFsmActorRef_must_proxy_receive_for_underlying_actor_with_sender()
        {
            var a = new TestFSMRef<FsmActor, TestFsmState, string>(Sys, Props.Create(() => new FsmActor(TestActor)));
            a.Receive("check");
            ExpectMsg("first");

            // verify that we can change state
            a.SetState(TestFsmState.Last);
            a.Receive("check");
            ExpectMsg("last");
        }

        [Fact]
        public void BugFix1709_TestFsmActorRef_must_work_with_Fsms_with_constructor_arguments()
        {
            var a = ActorOfAsTestFSMRef<FsmActor, TestFsmState, string>(Props.Create(() => new FsmActor(TestActor)));
            a.Receive("check");
            ExpectMsg("first");

            // verify that we can change state
            a.SetState(TestFsmState.Last);
            a.Receive("check");
            ExpectMsg("last");
        }

        private class SaveStringActor : TActorBase
        {
            public string ReceivedString { get; private set; }

            protected override bool ReceiveMessage(object message)
            {
                ReceivedString = message as string;
                return true;
            }

            public SaveStringActor(Thread parentThread, AtomicReference<Thread> otherThread) : base(parentThread, otherThread)
            { }
        }
    }
}

