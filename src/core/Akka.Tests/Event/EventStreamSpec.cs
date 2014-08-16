using Akka.Actor.Internals;
using Akka.TestKit;
using Akka.Util;
using Xunit;
using Akka.Tests.TestUtils;
using Akka.Actor;
using Akka.Event;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Tests.Event
{
    
    public class EventStreamSpec : AkkaSpec
    {
        public EventStreamSpec():base(GetConfig())
        {
        }

        public class M : Comparable
        {
            public int Value { get; set; }           
        }

        public class A : Comparable
        {

        }

        public class B1 : A
        {
            
        }

        public class B2 : A
        {
            
        }

        public class C : B1
        {
            //oh dear.. we should go for F# for this...
        }


        interface T {}
        interface AT : T{}
        interface  ATT : AT{}
        interface  BT : T{}
        interface  BTT : BT{}
        class CC {}
        class CCATBT : CC, ATT, BTT { }

        [Fact]
        public void ManageSubscriptions()
        {
            var bus = new EventStream(true);
            bus.Subscribe(TestActor, typeof(M));

            bus.Publish(new M { Value = 42 });
            ExpectMsg(new M { Value = 42 });
            bus.Unsubscribe(TestActor);
            bus.Publish(new M { Value = 43 });
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void NotAllowNullAsSubscriber()
        {
            var bus = new EventStream(true);
            XAssert.Throws<ArgumentNullException>(() =>
            {
                bus.Subscribe(null, typeof(M));
            });
        }

        [Fact]
        public void NotAllowNullAsUnsubscriber()
        {
            var bus = new EventStream(true);
            XAssert.Throws<ArgumentNullException>(() =>
            {
                bus.Unsubscribe(null, typeof(M));
            });
            XAssert.Throws<ArgumentNullException>(() =>
            {
                bus.Unsubscribe(null);
            });
        }

        [Fact]
        public void ManageSubChannelsUsingClasses()
        {
            var a = new A();
            var b1 = new B1();
            var b2 = new B2();
            var c = new C();
            var bus = new EventStream(false);
            bus.Subscribe(TestActor,typeof(B2));
            bus.Publish(c);
            bus.Publish(b2);
            ExpectMsg(b2);
            bus.Subscribe(TestActor, typeof(A));
            bus.Publish(c);
            ExpectMsg(c);
            bus.Publish(b1);
            ExpectMsg(b1);

            bus.Unsubscribe(TestActor, typeof(B1));
            bus.Publish(c); //should not publish
            bus.Publish(b2); //should publish
            bus.Publish(a); //should publish
            ExpectMsg(b2);
            ExpectMsg(a);
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }


        [Fact(DisplayName="manage sub-channels using classes and traits (update on subscribe)" )]
        public void ManageSubChannelsUsingClassesAndInterfacesUpdateOnSubscribe()
        {
            var es = new EventStream(false);
            var tm1 = new CC();
            var tm2 = new CCATBT();
            var a1= CreateTestProbe();
            var a2= CreateTestProbe();
            var a3 = CreateTestProbe();
            var a4 = CreateTestProbe();

            es.Subscribe(a1.Ref, typeof(AT)).ShouldBeTrue();
            es.Subscribe(a2.Ref, typeof(BT)).ShouldBeTrue();
            es.Subscribe(a3.Ref, typeof(CC)).ShouldBeTrue();
            es.Subscribe(a4.Ref, typeof(CCATBT)).ShouldBeTrue();
            es.Publish(tm1);
            es.Publish(tm2);
            a1.ExpectMsg((object) tm2);
            a2.ExpectMsg((object) tm2);
            a3.ExpectMsg((object) tm1);
            a3.ExpectMsg((object) tm2);
            a4.ExpectMsg((object) tm2);
            es.Unsubscribe(a1.Ref, typeof(AT)).ShouldBeTrue();
            es.Unsubscribe(a2.Ref, typeof(BT)).ShouldBeTrue();
            es.Unsubscribe(a3.Ref, typeof(CC)).ShouldBeTrue();
            es.Unsubscribe(a4.Ref, typeof(CCATBT)).ShouldBeTrue();
        }

        //"manage sub-channels using classes and traits (update on unsubscribe)"
        [Fact]
        public void ManageSubChannelsUsingClassesAndInterfacesUpdateOnUnsubscribe()
        {
            var es = new EventStream(false);
            var tm1 = new CC();
            var tm2 = new CCATBT();
            var a1 = CreateTestProbe();
            var a2 = CreateTestProbe();
            var a3 = CreateTestProbe();
            var a4 = CreateTestProbe();

            es.Subscribe(a1.Ref, typeof(AT));
            es.Subscribe(a2.Ref, typeof(BT));
            es.Subscribe(a3.Ref, typeof(CC));
            es.Subscribe(a4.Ref, typeof(CCATBT));
            es.Unsubscribe(a3.Ref, typeof(CC));
            es.Publish(tm1);
            es.Publish(tm2);
            a1.ExpectMsg((object) tm2);
            a2.ExpectMsg((object) tm2);
            a3.ExpectNoMsg(TimeSpan.FromSeconds(1));
            a4.ExpectMsg((object) tm2);
            es.Unsubscribe(a1.Ref, typeof(AT)).ShouldBeTrue();
            es.Unsubscribe(a2.Ref, typeof(BT)).ShouldBeTrue();
            es.Unsubscribe(a3.Ref, typeof(CC)).ShouldBeFalse();
            es.Unsubscribe(a4.Ref, typeof(CCATBT)).ShouldBeTrue();
        }

        [Fact]
        public void ManageSubChannelsUsingClassesAndInterfacesUpdateOnUnsubscribeAll()
        {
            var es = new EventStream(false);
            var tm1 = new CC();
            var tm2 = new CCATBT();
            var a1 = CreateTestProbe();
            var a2 = CreateTestProbe();
            var a3 = CreateTestProbe();
            var a4 = CreateTestProbe();

            es.Subscribe(a1.Ref, typeof(AT)).ShouldBeTrue();
            es.Subscribe(a2.Ref, typeof(BT)).ShouldBeTrue();
            es.Subscribe(a3.Ref, typeof(CC)).ShouldBeTrue();
            es.Subscribe(a4.Ref, typeof(CCATBT)).ShouldBeTrue();
            es.Unsubscribe(a3.Ref).ShouldBeTrue();
            es.Publish(tm1);
            es.Publish(tm2);
            a1.ExpectMsg((object) tm2);
            a2.ExpectMsg((object) tm2);
            a3.ExpectNoMsg(TimeSpan.FromSeconds(1));
            a4.ExpectMsg((object) tm2);
            es.Unsubscribe(a1.Ref, typeof(AT)).ShouldBeTrue();
            es.Unsubscribe(a2.Ref, typeof(BT)).ShouldBeTrue();
            es.Unsubscribe(a3.Ref, typeof(CC)).ShouldBeFalse();
            es.Unsubscribe(a4.Ref, typeof(CCATBT)).ShouldBeTrue();
        }

        public class SetTarget
        {
            public ActorRef Ref { get; private set; }
            public SetTarget(ActorRef @ref)
            {
                this.Ref = @ref;
            }
        }

        [Fact(Skip = "TODO: this test hangs, why?")]
        public void ManageLogLevels()
        {
            var bus = new EventStream(false);
            bus.StartDefaultLoggers((ActorSystemImpl) Sys);
            bus.Publish(new SetTarget(TestActor));
            ExpectMsg("OK",TimeSpan.FromSeconds(5));

            verifyLevel(bus, LogLevel.InfoLevel);
            bus.SetLogLevel(LogLevel.WarningLevel);
            verifyLevel(bus, LogLevel.WarningLevel);
            bus.SetLogLevel(LogLevel.DebugLevel);
            verifyLevel(bus, LogLevel.DebugLevel);
            bus.SetLogLevel(LogLevel.ErrorLevel);
            verifyLevel(bus, LogLevel.ErrorLevel);

        }

        private static string GetConfig()
        {
            return @"
akka {
        actor.serialize-messages = off
        stdout-loglevel = WARNING
        loglevel = INFO
        loggers = [""%logger%""]
      }
".Replace("%logger%", typeof(MyLog).AssemblyQualifiedName);

        }

        public class MyLog : UntypedActor
        {
            private ActorRef dst = Context.System.DeadLetters;

            protected override void OnReceive(object message)
            {
                PatternMatch.Match(message)
                .With<InitializeLogger>(m =>
                {
                    var bus = m.LoggingBus;
                    bus.Subscribe(this.Self, typeof(SetTarget));
                    bus.Subscribe(this.Self, typeof(UnhandledMessage));

                    Sender.Tell(new LoggerInitialized());
                })
                .With<SetTarget>(m =>
                {
                    dst = m.Ref;
                    dst.Tell("OK");
                })
                .With<LogEvent>(m => dst.Tell(m))
                .With<UnhandledMessage>(m => dst.Tell(m));
            }
        }

        private void verifyLevel(LoggingBus bus,LogLevel level) 
        {
            var allmsg = new LogEvent[] {
                new Debug("", null, "debug"),
                new Info("", null, "info"),
                new  Warning("", null, "warning"),
                new Error(null,"", null, "error")};

            var msg = allmsg.Where(l => l.LogLevel() >= level);
            allmsg.ToList().ForEach(l => bus.Publish(l));
            msg.ToList().ForEach(l => ExpectMsg(l));
          }
    }
}
