//-----------------------------------------------------------------------
// <copyright file="ActorSystemSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Xunit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Xml;
using Akka.Configuration;
using Xunit.Abstractions;

namespace Akka.Tests.Actor
{
    
    public class ActorSystemSpec : AkkaSpec
    {

        public ActorSystemSpec(ITestOutputHelper output)
            : base(@"akka.extensions = [""Akka.Tests.Actor.TestExtension,Akka.Tests""]", output)
        {
        }
       

        [Fact]
        public void AnActorSystemMustRejectInvalidNames()
        {
            new List<string> { 
                  "hallo_welt",
                  "-hallowelt",
                  "hallo*welt",
                  "hallo@welt",
                  "hallo#welt",
                  "hallo$welt",
                  "hallo%welt",
                  "hallo/welt"}.ForEach(n =>
                  {
                      XAssert.Throws<ArgumentException>(() => ActorSystem.Create(n));
                  });
        }

        [Fact]
        public void AnActorSystemMustAllowValidNames()
        {
            ActorSystem
                .Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-")
                .Terminate();
        }

        [Fact]
        public void AnActorSystemShouldBeAllowedToBlockUntilExit()
        {
            var actorSystem = ActorSystem
                .Create(Guid.NewGuid().ToString());
            var st = Stopwatch.StartNew();
            var asyncShutdownTask = Task.Delay(TimeSpan.FromSeconds(1)).ContinueWith(_ => actorSystem.Terminate());
            actorSystem.WhenTerminated.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue();
            Assert.True(st.Elapsed.TotalSeconds >= .9);
        }

        [Fact]
        public void Given_a_system_that_isnt_going_to_shutdown_When_waiting_for_system_shutdown_Then_it_times_out()
        {
            var actorSystem = ActorSystem.Create(Guid.NewGuid().ToString());
            actorSystem.WhenTerminated.Wait(TimeSpan.FromMilliseconds(10)).ShouldBeFalse();
        }

        [Fact]
        public void Run_termination_callbacks_in_order()
        {
            var actorSystem = ActorSystem.Create(Guid.NewGuid().ToString());
            var result = new List<int>();
            var expected = new List<int>();
            var count = 10;
            var latch = new TestLatch(count);

            for (int i = 0; i < count; i++)
            {
                expected.Add(i);

                var value = i;
                actorSystem.RegisterOnTermination(() =>
                {
                    Task.Delay(Dilated(TimeSpan.FromMilliseconds(value % 3))).Wait();
                    result.Add(value);
                    latch.CountDown();
                });
            }

            actorSystem.Terminate();
            latch.Ready();

            expected.Reverse();

            Assert.Equal(expected, result);
        }

        [Fact]
        public void AwaitTermination_after_termination_callbacks()
        {
            var actorSystem = ActorSystem.Create(Guid.NewGuid().ToString());
            var callbackWasRun = false;

            actorSystem.RegisterOnTermination(() =>
            {
                Task.Delay(Dilated(TimeSpan.FromMilliseconds(50))).Wait();
                callbackWasRun = true;
            });

            new TaskFactory().StartNew(() =>
            {
                Task.Delay(Dilated(TimeSpan.FromMilliseconds(200))).Wait();
                actorSystem.Terminate();
            });

            actorSystem.WhenTerminated.Wait(TimeSpan.FromSeconds(5));
            Assert.True(callbackWasRun);
        }

        [Fact]
        public void Throw_exception_when_register_callback_after_shutdown()
        {
            var actorSystem = ActorSystem.Create(Guid.NewGuid().ToString());

            actorSystem.Terminate().Wait(TimeSpan.FromSeconds(10));
            
            var ex = Assert.Throws<Exception>(() => actorSystem.RegisterOnTermination(() => { }));
            Assert.Equal("ActorSystem already terminated.", ex.Message);
        }

        [Fact]
        public void AnActorSystem_Must_Allow_Configuration_Of_Guardian_Supervisor_Strategy()
        {
            var config = ConfigurationFactory
                .ParseString(@"akka.actor.guardian-supervisor-strategy = ""Akka.Actor.StoppingSupervisorStrategy""")
                .WithFallback(DefaultConfig);

            var actorSystem = ActorSystem.Create("Stop", config);
            var a = actorSystem.ActorOf(Props.Create<ThrowerActor>());
            var probe = CreateTestProbe("guardian-test-observer");
            probe.Watch(a);

            // TODO: EventFilter works with Sys actor system only.
            //EventFilter.Exception(typeof(Exception)).ExpectOne(() =>
            //{
                a.Tell("die");
            //});

            var terminated = probe.ExpectMsg<Terminated>();
            Assert.True(terminated.ExistenceConfirmed, "terminated.ExistenceConfirmed should equal true");
            Assert.False(terminated.AddressTerminated, "terminated.AddressTerminated should equal false");
            Shutdown(actorSystem);
        }

        [Fact]
        public void AnActorSystem_Must_Shut_Down_When_User_Guardian_Escalates()
        {
            var config = ConfigurationFactory
                .ParseString(@"akka.actor.guardian-supervisor-strategy = ""Akka.Tests.Actor.ActorSystemSpec+Strategy, Akka.Tests""")
                .WithFallback(DefaultConfig);

            var actorSystem = ActorSystem.Create("Stop", config);
            var a = actorSystem.ActorOf(Props.Create<ThrowerActor>());

            // TODO: EventFilter works with Sys actor system only.
            //EventFilter.Exception(typeof(Exception)).ExpectOne(() =>
            //{
            a.Tell("die");
            //});

            var result = Task.WaitAny(actorSystem.TerminationTask, Task.Delay(GetTimeoutOrDefault(null)));
            Assert.Equal(0, result);
        }

        private class ThrowerActor : ReceiveActor
        {
            public ThrowerActor()
            {
                Receive<string>(str =>
                {
                    if (str == "die")
                    {
                        throw new Exception("Hello");
                    }
                });
            }
        }

        private class Strategy : ISupervisorStrategyConfigurator
        {
            public SupervisorStrategy Create()
            {
                // TODO: Akka contains SupervisorStrategy.Escalate
                return new OneForOneStrategy(Decider.From(e => Directive.Escalate));
            }
        }

        #region Extensions tests

        [Fact]
        public void AnActorSystem_Must_Support_Extensions()
        {
            Assert.True(Sys.HasExtension<TestExtensionImpl>());
            var testExtension = Sys.WithExtension<TestExtensionImpl>();
            Assert.Equal(Sys, testExtension.System);
        }

        [Fact]
        public void AnActorSystem_Must_Support_Dynamically_Registered_Extensions()
        {
            Assert.False(Sys.HasExtension<OtherTestExtensionImpl>());
            var otherTestExtension = Sys.WithExtension<OtherTestExtensionImpl>(typeof(OtherTestExtension));
            Assert.True(Sys.HasExtension<OtherTestExtensionImpl>());
            Assert.Equal(Sys, otherTestExtension.System);
        }

        [Fact]
        public void AnActorSystem_Must_Setup_The_Default_Scheduler()
        {
            Assert.True(Sys.Scheduler.GetType() == typeof(DedicatedThreadScheduler));
        }

        [Fact]
        public void AnActorSystem_Must_Support_Using_A_Customer_Scheduler()
        {
            var actorSystem = ActorSystem.Create(Guid.NewGuid().ToString(), DefaultConfig.WithFallback("akka.scheduler.implementation = \"Akka.Tests.Actor.TestScheduler, Akka.Tests\""));
            Assert.True(actorSystem.Scheduler.GetType() == typeof(TestScheduler));
        }

        #endregion
    }

    public class OtherTestExtension : ExtensionIdProvider<OtherTestExtensionImpl>
    {
        public override OtherTestExtensionImpl CreateExtension(ExtendedActorSystem system)
        {
            return new OtherTestExtensionImpl(system);
        }
    }

    public class OtherTestExtensionImpl : IExtension
    {
        public OtherTestExtensionImpl(ActorSystem system)
        {
            System = system;
        }

        public ActorSystem System { get; private set; }
    }

    public class TestExtension : ExtensionIdProvider<TestExtensionImpl>
    {
        public override TestExtensionImpl CreateExtension(ExtendedActorSystem system)
        {
            return new TestExtensionImpl(system);
        }
    }

    public class TestExtensionImpl : IExtension
    {
        public TestExtensionImpl(ActorSystem system)
        {
            System = system;
        }

        public ActorSystem System { get; private set; }
    }

    public class TestScheduler : IScheduler
    {
        public TestScheduler(ActorSystem system)
        {
            
        }

        public void ScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender)
        {
            throw new NotImplementedException();
        }

        public void ScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender, ICancelable cancelable)
        {
            throw new NotImplementedException();
        }

        public void ScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message,
            IActorRef sender)
        {
            throw new NotImplementedException();
        }

        public void ScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message,
            IActorRef sender, ICancelable cancelable)
        {
            throw new NotImplementedException();
        }

        public DateTimeOffset Now { get; private set; }
        public TimeSpan MonotonicClock { get; private set; }
        public TimeSpan HighResMonotonicClock { get; private set; }
        public IAdvancedScheduler Advanced { get; private set; }
    }
}

