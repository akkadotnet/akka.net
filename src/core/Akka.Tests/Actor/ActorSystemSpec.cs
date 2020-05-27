//-----------------------------------------------------------------------
// <copyright file="ActorSystemSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Actor.Internal;
using Akka.TestKit;
using Xunit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using FluentAssertions.Execution;

namespace Akka.Tests.Actor
{

    public class ActorSystemSpec : AkkaSpec
    {
        public ActorSystemSpec()
            : base(@"akka.extensions = [""Akka.Tests.Actor.TestExtension,Akka.Tests""]
                     slow { 
                        type = ""Akka.Tests.Actor.SlowDispatcher, Akka.Tests"" 
                     }")
        {
        }


        [Fact]
        public void Reject_invalid_names()
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

        /// <summary>
        /// For additional info please check the original documentation.
        /// http://doc.akka.io/docs/akka/2.4/scala/logging.html#Auxiliary_logging_options
        /// akka {
        ///     # Log the complete configuration at INFO level when the actor system is started.
        ///     # This is useful when you are uncertain of what configuration is used.
        ///     log-config-on-start = on
        /// }
        /// </summary>
        [Fact]
        public void Logs_config_on_start_with_info_level()
        {
            var config = ConfigurationFactory.ParseString("akka.log-config-on-start = on")
                .WithFallback(DefaultConfig);

            var system = new ActorSystemImpl(Guid.NewGuid().ToString(), config, ActorSystemSetup.Empty);
            // Actor system should be started to attach the EventFilterFactory
            system.Start();

            var eventFilter = new EventFilterFactory(new TestKit.Xunit2.TestKit(system));

            // Notice here we forcedly start actor system again to monitor how it processes
            var expected = "log-config-on-start : on";
            eventFilter.Info(contains:expected).ExpectOne(() => system.Start());

            system.Terminate();
        }

        [Fact]
        public void Does_not_log_config_on_start()
        {
            var config = ConfigurationFactory.ParseString("akka.log-config-on-start = off")
                .WithFallback(DefaultConfig);

            var system = new ActorSystemImpl(Guid.NewGuid().ToString(), config, ActorSystemSetup.Empty);
            // Actor system should be started to attach the EventFilterFactory
            system.Start();

            var eventFilter = new EventFilterFactory(new TestKit.Xunit2.TestKit(system));

            // Notice here we forcedly start actor system again to monitor how it processes
            eventFilter.Info().Expect(0, () => system.Start());

            system.Terminate();
        }

        [Fact]
        public void Allow_valid_names()
        {
            ActorSystem
                .Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-")
                .Terminate();
        }

        [Fact]
        public void Log_dead_letters()
        {
            var sys = ActorSystem.Create("LogDeadLetters", ConfigurationFactory.ParseString("akka.loglevel=INFO")
                .WithFallback(DefaultConfig));

            try
            {
                var a = sys.ActorOf(Props.Create<Terminater>());

                var eventFilter = new EventFilterFactory(new TestKit.Xunit2.TestKit(sys));
                eventFilter.Info(contains: "not delivered").Expect(1, () =>
                {
                    a.Tell("run");
                    a.Tell("boom");
                });
            }
            finally { Shutdown(sys); }
        }

        [Fact]
        public void Block_until_exit()
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
            
            var ex = Assert.Throws<InvalidOperationException>(() => actorSystem.RegisterOnTermination(() => { }));
            Assert.Equal("ActorSystem already terminated.", ex.Message);
        }

        [Fact]
        public void Reliably_create_waves_of_actors()
        {
            var timeout = Dilated(TimeSpan.FromSeconds(20));
            var waves = Task.WhenAll(
                Sys.ActorOf(Props.Create<Wave>()).Ask<string>(50000),
                Sys.ActorOf(Props.Create<Wave>()).Ask<string>(50000),
                Sys.ActorOf(Props.Create<Wave>()).Ask<string>(50000));

            waves.Wait(timeout.Duration() + TimeSpan.FromSeconds(5));

            Assert.Equal(new[] { "done", "done", "done" }, waves.Result);
        }

        [Fact]
        public void Find_actors_that_just_have_been_created()
        {
            Sys.ActorOf(Props.Create(() => new FastActor(new TestLatch(), TestActor)).WithDispatcher("slow"));
            Assert.Equal(typeof(LocalActorRef), ExpectMsg<Type>());
        }

        [Fact()]
        public void Reliable_deny_creation_of_actors_while_shutting_down()
        {
            var sys = ActorSystem.Create("DenyCreationWhileShuttingDown");
            sys.Scheduler.Advanced.ScheduleOnce(TimeSpan.FromMilliseconds(100), () =>
            {
                sys.Terminate();
            });
            var failing = false;
            var created = new HashSet<IActorRef>();

            while (!sys.WhenTerminated.IsCompleted)
            {
                try
                {
                    var t = sys.ActorOf<Terminater>();
                    Assert.False(failing); // because once failing => always failing (it’s due to shutdown)
                    created.Add(t);

                    if (created.Count % 1000 == 0)
                        Thread.Sleep(50); // in case of unfair thread scheduling
                }
                catch (InvalidOperationException)
                {
                    failing = true;
                }

                
                if (!failing && sys.Uptime.TotalSeconds >= 10)
                    throw new AssertionFailedException(created.Last() + Environment.NewLine +
                                                       "System didn't terminate within 5 seconds");
            }

            var nonTerminatedOrNonstartedActors = created.Cast<ActorRefWithCell>()
                .Where(actor => !actor.IsTerminated && !(actor.Underlying is UnstartedCell)).ToList();
            Assert.Empty(nonTerminatedOrNonstartedActors);
        }

        #region Extensions tests

        [Fact]
        public void Support_extensions()
        {
            Assert.True(Sys.HasExtension<TestExtensionImpl>());
            var testExtension = Sys.WithExtension<TestExtensionImpl>();
            Assert.Equal(Sys, testExtension.System);
        }

        [Fact]
        public void Support_dynamically_registered_extensions()
        {
            Assert.False(Sys.HasExtension<OtherTestExtensionImpl>());
            var otherTestExtension = Sys.WithExtension<OtherTestExtensionImpl>(typeof(OtherTestExtension));
            Assert.True(Sys.HasExtension<OtherTestExtensionImpl>());
            Assert.Equal(Sys, otherTestExtension.System);
        }

        [Fact]

        public void Handle_extensions_that_fail_to_initialize()
        {
            Action loadExtensions = () => Sys.WithExtension<FailingTestExtensionImpl>(typeof(FailingTestExtension));

            Assert.Throws<FailingTestExtension.TestException>(loadExtensions);
            // same exception should be reported next time
            Assert.Throws<FailingTestExtension.TestException>(loadExtensions);
        }

        #endregion

        [Fact]
        public void Setup_the_default_scheduler()
        {
            Assert.True(Sys.Scheduler.GetType() == typeof(HashedWheelTimerScheduler));
        }

        [Fact]
        public void Support_using_a_custom_scheduler()
        {
            var actorSystem = ActorSystem.Create(Guid.NewGuid().ToString(), DefaultConfig.WithFallback("akka.scheduler.implementation = \"Akka.Tests.Actor.TestScheduler, Akka.Tests\""));
            Assert.True(actorSystem.Scheduler.GetType() == typeof(TestScheduler));
        }

        [Fact]
        public void Allow_configuration_of_guardian_supervisor_strategy()
        {
            var config = ConfigurationFactory.ParseString("akka.actor.guardian-supervisor-strategy=\"Akka.Actor.StoppingSupervisorStrategy\"")
                .WithFallback(DefaultConfig);

            var system = ActorSystem.Create(Guid.NewGuid().ToString(), config);

            var a = system.ActorOf(actor =>
            {
                actor.Receive<string>((msg, context) => { throw new Exception("Boom"); });
            });

            var probe = CreateTestProbe(system);
            probe.Watch(a);

            a.Tell("die");

            var t = probe.ExpectTerminated(a);

            Assert.True(t.ExistenceConfirmed);
            Assert.False(t.AddressTerminated);

            system.Terminate();
        }

        [Fact]
        public void Shutdown_when_userguardian_escalates()
        {
            var config = ConfigurationFactory.ParseString("akka.actor.guardian-supervisor-strategy=\"Akka.Tests.Actor.TestStrategy, Akka.Tests\"")
                .WithFallback(DefaultConfig);

            var system = ActorSystem.Create(Guid.NewGuid().ToString(), config);

            var a = system.ActorOf(actor =>
            {
                actor.Receive<string>((msg, context) => { throw new Exception("Boom"); });
            });

            a.Tell("die");

            Assert.True(system.WhenTerminated.Wait(1000));
        }
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

    public class FailingTestExtension : ExtensionIdProvider<FailingTestExtensionImpl>
    {
        public override FailingTestExtensionImpl CreateExtension(ExtendedActorSystem system)
        {
            return new FailingTestExtensionImpl(system);
        }

        public class TestException : Exception
        {
        }
    }

    public class FailingTestExtensionImpl : IExtension
    {
        public FailingTestExtensionImpl(ActorSystem system)
        {
            // first time the actor is created
            var uniqueActor = system.ActorOf(Props.Empty, "uniqueActor");
            // but the extension initialization fails
            // second time it will throw exception when trying to create actor with same name,
            // but we want to see the first exception every time
            throw new FailingTestExtension.TestException();
        }
    }

    public class TestScheduler : IScheduler
    {
        public TestScheduler(Config config, ILoggingAdapter log)
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

    public class Wave : ReceiveActor
    {
        private IActorRef _master = Nobody.Instance;
        private readonly HashSet<IActorRef> _terminaters = new HashSet<IActorRef>();

        public Wave()
        {
            Receive<int>(n =>
            {
                _master = Sender;

                for (int i = 0; i < n; i++)
                {
                    var man = Context.Watch(Context.System.ActorOf(Props.Create<Terminater>()));
                    man.Tell("run");
                    _terminaters.Add(man);
                }
            });

            Receive<Terminated>(t =>
            {
                var child = t.ActorRef;

                if (_terminaters.Contains(child))
                    _terminaters.Remove(child);

                if (_terminaters.Count == 0)
                {
                    _master.Tell("done");
                    Context.Stop(Self);
                }
            });
        }

        protected override void PreRestart(Exception reason, object message)
        {
            if (!_master.IsNobody())
                _master.Tell($"failed with {reason} while processing {message}");

            Context.Stop(Self);
        }
    }

    public class Terminater : ReceiveActor
    {
        public Terminater()
        {
            Receive<string>(s => "run".Equals(s), s => Context.Stop(Self));
        }
    }

    public class TestStrategy : SupervisorStrategyConfigurator
    {
        public override SupervisorStrategy Create()
        {
            return new OneForOneStrategy(ex => Directive.Escalate);
        }
    }

    public class SlowDispatcher : MessageDispatcherConfigurator
    {
        private readonly DispatcherImpl _instance;

        public SlowDispatcher(Config config, IDispatcherPrerequisites prerequisites) : base(config, prerequisites)
        {
            _instance = new DispatcherImpl(this);
        }
        
        public override MessageDispatcher Dispatcher() => _instance;

        private class DispatcherImpl : MessageDispatcher
        {
            private TestLatch _latch;

            public DispatcherImpl(MessageDispatcherConfigurator configurator) : base(configurator)
            {
            }

            protected override void ExecuteTask(IRunnable run)
            {
                run.Run();
                _latch.Ready(TimeSpan.FromSeconds(1));
            }

            protected override void Shutdown()
            {
                // do nothing
            }

            public override void Attach(ActorCell cell)
            {
                if (cell.Props.Type == typeof (FastActor))
                    _latch = cell.Props.Arguments[0] as TestLatch;

                base.Attach(cell);
            }
        }
    }

    public class FastActor : UntypedActor
    {
        public FastActor(TestLatch testLatch, IActorRef testActor)
        {
            var ref1 = Context.ActorOf(Props.Empty);
            var ref2 = Context.Child(ref1.Path.Name);

            testActor.Tell(ref2.GetType());
            testLatch.CountDown();
        }

        protected override void OnReceive(object message) { }
    }
}

