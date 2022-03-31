//-----------------------------------------------------------------------
// <copyright file="EventStreamSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Event;
using Akka.TestKit;
using Akka.Tests.TestUtils;
using System;
using System.Linq;
using Akka.Util.Internal;
using Xunit;
using System.Threading.Tasks;

namespace Akka.Tests.Event
{
    public class EventStreamSpec : AkkaSpec
    {
        public EventStreamSpec()
            : base(GetConfig())
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

        private interface T { }

        private interface AT : T { }

        private interface ATT : AT { }

        private interface BT : T { }

        private interface BTT : BT { }

        private class CC { }

        private class CCATBT : CC, ATT, BTT { }

        [Fact]
        public async Task Manage_subscriptions()
        {

            var bus = new EventStream(true);
            bus.StartUnsubscriber(Sys.AsInstanceOf<ActorSystemImpl>());
            bus.Subscribe(TestActor, typeof(M));

            bus.Publish(new M { Value = 42 });
            await ExpectMsgAsync(new M { Value = 42 });
            bus.Unsubscribe(TestActor);
            bus.Publish(new M { Value = 43 });
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void Not_allow_null_as_subscriber()
        {
            var bus = new EventStream(true);
            Assert.Throws<ArgumentNullException>(() =>
            {
                bus.Subscribe(null, typeof(M));
            });
        }

        [Fact]
        public void Not_allow_null_as_unsubscriber()
        {
            var bus = new EventStream(true);
            Assert.Throws<ArgumentNullException>(() =>
            {
                bus.Unsubscribe(null, typeof(M));
            });
            Assert.Throws<ArgumentNullException>(() =>
            {
                bus.Unsubscribe(null);
            });
        }

        [Fact]
        public async Task Be_able_to_log_unhandled_messages()
        {
            using (var system = ActorSystem.Create("EventStreamSpecUnhandled", GetDebugUnhandledMessagesConfig()))
            {
                system.EventStream.Subscribe(TestActor, typeof(Debug));

                var msg = new UnhandledMessage(42, system.DeadLetters, system.DeadLetters);

                system.EventStream.Publish(msg);

                var debugMsg = await ExpectMsgAsync<Debug>();

                debugMsg.Message.ToString().StartsWith("Unhandled message from").ShouldBeTrue();
                debugMsg.Message.ToString().EndsWith(": 42").ShouldBeTrue();
            }
        }

        /// <summary>
        /// Reproduction spec for https://github.com/akkadotnet/akka.net/issues/3267
        /// </summary>
        [Fact]
        public async Task Bugfix3267_able_to_log_unhandled_messages_with_nosender()
        {
            using (var system = ActorSystem.Create("EventStreamSpecUnhandled", GetDebugUnhandledMessagesConfig()))
            {
                system.EventStream.Subscribe(TestActor, typeof(Debug));

                // sender is NoSender
                var msg = new UnhandledMessage(42, ActorRefs.NoSender, system.DeadLetters);

                system.EventStream.Publish(msg);

                var debugMsg = await ExpectMsgAsync<Debug>();

                debugMsg.Message.ToString().StartsWith("Unhandled message from").ShouldBeTrue();
                debugMsg.Message.ToString().EndsWith(": 42").ShouldBeTrue();
            }
        }

        [Fact]
        public async Task Manage_sub_channels_using_classes()
        {
            var a = new A();
            var b1 = new B1();
            var b2 = new B2();
            var c = new C();
            var bus = new EventStream(false);
            bus.Subscribe(TestActor, typeof(B2));
            bus.Publish(c);
            bus.Publish(b2);
            await ExpectMsgAsync(b2);
            bus.Subscribe(TestActor, typeof(A));
            bus.Publish(c);
            await ExpectMsgAsync(c);
            bus.Publish(b1);
            await ExpectMsgAsync(b1);

            bus.Unsubscribe(TestActor, typeof(B1));
            bus.Publish(c); //should not publish
            bus.Publish(b2); //should publish
            bus.Publish(a); //should publish
            await ExpectMsgAsync(b2);
            await ExpectMsgAsync(a);
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
        }

        [Fact(DisplayName = "manage sub-channels using classes and traits (update on subscribe)")]
        public async Task Manage_sub_channels_using_classes_and_interfaces_update_on_subscribe()
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
            es.Publish(tm1);
            es.Publish(tm2);
            await a1.ExpectMsgAsync((object)tm2);
            await a2.ExpectMsgAsync((object)tm2);
            await a3.ExpectMsgAsync((object)tm1);
            await a3.ExpectMsgAsync((object)tm2);
            await a4.ExpectMsgAsync((object)tm2);
            es.Unsubscribe(a1.Ref, typeof(AT)).ShouldBeTrue();
            es.Unsubscribe(a2.Ref, typeof(BT)).ShouldBeTrue();
            es.Unsubscribe(a3.Ref, typeof(CC)).ShouldBeTrue();
            es.Unsubscribe(a4.Ref, typeof(CCATBT)).ShouldBeTrue();
        }

        //"manage sub-channels using classes and traits (update on unsubscribe)"
        [Fact]
        public async Task Manage_sub_channels_using_classes_and_interfaces_update_on_unsubscribe()
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
            await a1.ExpectMsgAsync((object)tm2);
            await a2.ExpectMsgAsync((object)tm2);
            await a3.ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
            await a4.ExpectMsgAsync((object)tm2);
            es.Unsubscribe(a1.Ref, typeof(AT)).ShouldBeTrue();
            es.Unsubscribe(a2.Ref, typeof(BT)).ShouldBeTrue();
            es.Unsubscribe(a3.Ref, typeof(CC)).ShouldBeFalse();
            es.Unsubscribe(a4.Ref, typeof(CCATBT)).ShouldBeTrue();
        }

        [Fact]
        public async Task Manage_sub_channels_using_classes_and_interfaces_update_on_unsubscribe_all()
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
            await a1.ExpectMsgAsync((object)tm2);
            await a2.ExpectMsgAsync((object)tm2);
            await a3.ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
            await a4.ExpectMsgAsync((object)tm2);
            es.Unsubscribe(a1.Ref, typeof(AT)).ShouldBeTrue();
            es.Unsubscribe(a2.Ref, typeof(BT)).ShouldBeTrue();
            es.Unsubscribe(a3.Ref, typeof(CC)).ShouldBeFalse();
            es.Unsubscribe(a4.Ref, typeof(CCATBT)).ShouldBeTrue();
        }

        public class SetTarget
        {
            public IActorRef Ref { get; private set; }

            public SetTarget(IActorRef @ref)
            {
                this.Ref = @ref;
            }
        }

        [Fact]
        public async Task Manage_log_levels()
        {
            var bus = new EventStream(false);
            bus.StartDefaultLoggers((ActorSystemImpl)Sys);
            bus.Publish(new SetTarget(TestActor));
            await ExpectMsgAsync("OK", TimeSpan.FromSeconds(5));

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

        private static string GetDebugUnhandledMessagesConfig()
        {
            return @"
                akka {
                    actor.serialize-messages = off
                    actor.debug.unhandled = on
                    log-dead-letters = off
                    stdout-loglevel = DEBUG
                    loglevel = DEBUG
                    loggers = [""%logger%""]
                }
                ".Replace("%logger%", typeof(MyLog).AssemblyQualifiedName);
        }

        public class MyLog : ReceiveActor
        {
            private IActorRef dst = Context.System.DeadLetters;
            public MyLog()
            {
                Receive<InitializeLogger>(m =>
                {
                    var bus = m.LoggingBus;
                    bus.Subscribe(this.Self, typeof(SetTarget));
                    bus.Subscribe(this.Self, typeof(UnhandledMessage));

                    Sender.Tell(new LoggerInitialized());
                });
                Receive<SetTarget>(m =>
                {
                    dst = m.Ref;
                    dst.Tell("OK");
                });
                Receive<LogEvent>(m => dst.Tell(m));
                Receive<UnhandledMessage>(m => dst.Tell(m));
            }
        }

        private void verifyLevel(LoggingBus bus, LogLevel level)
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

