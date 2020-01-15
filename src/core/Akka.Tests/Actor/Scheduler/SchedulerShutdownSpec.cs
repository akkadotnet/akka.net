//-----------------------------------------------------------------------
// <copyright file="SchedulerShutdownSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Actor.Scheduler
{
    /// <summary>
    /// Need to guarantee that the <see cref="IScheduler"/>, if it implements <see cref="IDisposable"/>, is shutdown.
    /// </summary>
    public class SchedulerShutdownSpec
    {
        /// <summary>
        /// Runs on the calling thread. Mostly used for testing shutdown behavior.
        /// </summary>
        private class ShutdownScheduler : SchedulerBase, IDisposable
        {
            public readonly AtomicCounter Shutdown = new AtomicCounter(0);

            

            protected override DateTimeOffset TimeNow { get; }
            public override TimeSpan MonotonicClock { get; }
            public override TimeSpan HighResMonotonicClock { get; }

            protected override void InternalScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender,
                ICancelable cancelable)
            {
                receiver.Tell(message, sender);
            }

            protected override void InternalScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message,
                IActorRef sender, ICancelable cancelable)
            {
                receiver.Tell(message, sender);
            }

            protected override void InternalScheduleOnce(TimeSpan delay, Action action, ICancelable cancelable)
            {
                action();
            }

            protected override void InternalScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action, ICancelable cancelable)
            {
                action();
            }

            public void Dispose()
            {
                Shutdown.IncrementAndGet();
            }

            public ShutdownScheduler(Config scheduler, ILoggingAdapter log) : base(scheduler, log)
            {
            }
        }

        public static readonly Config Config = ConfigurationFactory.ParseString("akka.scheduler.implementation = \""+ typeof(ShutdownScheduler).AssemblyQualifiedName + "\"");

        [Fact]
        public void ActorSystem_must_terminate_scheduler_on_shutdown()
        {
            ActorSystem sys = null;
            try
            {
                sys = ActorSystem.Create("SchedulerShutdownSys1", Config);
                var scheduler = (ShutdownScheduler)sys.Scheduler;
                var currentCounter = scheduler.Shutdown.Current;
                sys.Terminate().Wait(sys.Settings.SchedulerShutdownTimeout).Should().BeTrue();
                var nextCounter = scheduler.Shutdown.Current;
                nextCounter.Should().Be(currentCounter + 1);
            }
            finally
            {
                sys?.Terminate().Wait(TimeSpan.FromSeconds(5));
            }
        }

        [Fact]
        public void ActorSystem_must_terminate_scheduler_with_queued_work_on_shutdown()
        {
            ActorSystem sys = null;
            try
            {
                var i = 0;
                sys = ActorSystem.Create("SchedulerShutdownSys1", Config);
                var scheduler = (ShutdownScheduler)sys.Scheduler;
                sys.Scheduler.Advanced.ScheduleRepeatedly(TimeSpan.FromMilliseconds(0), TimeSpan.FromMilliseconds(10), () => i++);
                Task.Delay(100).Wait(); // give the scheduler a chance to start and run
                var currentCounter = scheduler.Shutdown.Current;
                sys.Terminate().Wait(sys.Settings.SchedulerShutdownTimeout).Should().BeTrue();
                var nextCounter = scheduler.Shutdown.Current;
                nextCounter.Should().Be(currentCounter + 1);
                var stoppedValue = i;
                stoppedValue.Should().BeGreaterThan(0, "should have incremented at least once");
                Task.Delay(100).Wait();
                i.Should().Be(stoppedValue, "Scheduler shutdown; should not still be incrementing values.");
            }
            finally
            {
                sys?.Terminate().Wait(TimeSpan.FromSeconds(5));
            }
        }

        [Fact]
        public void ActorSystem_default_scheduler_mustbe_able_to_terminate_on_shutdown()
        {
            ActorSystem sys = ActorSystem.Create("SchedulerShutdownSys2");
            Assert.True(sys.Scheduler is IDisposable);
            sys.Terminate().Wait(TimeSpan.FromSeconds(5));
        }


        private class MyScheduledActor : ReceiveActor
        {
            private bool _received = false;

            public MyScheduledActor()
            {
                Receive<string>(str => str.Equals("get"), str =>
                {
                    Sender.Tell(_received);
                });

                Receive<string>(str => str.Equals("set"), str =>
                {
                    _received = true;
                });
            }
        }

        [Fact]
        public void ActorSystem_default_scheduler_must_never_accept_more_work_after_shutdown()
        {
            ActorSystem sys = ActorSystem.Create("SchedulerShutdownSys3");
            var receiver = sys.ActorOf(Props.Create(() => new MyScheduledActor()));
            sys.Scheduler.ScheduleTellOnce(0, receiver, "set", ActorRefs.NoSender);
            Thread.Sleep(50); // let the scheduler run
            Assert.True(receiver.Ask<bool>("get", TimeSpan.FromMilliseconds(100)).Result);

            if(!sys.Terminate().Wait(TimeSpan.FromSeconds(5)))
                Assert.True(false, $"Expected ActorSystem to terminate within 5s. Took longer.");

            Assert.Throws<SchedulerException>(() =>
            {
                sys.Scheduler.ScheduleTellOnce(TimeSpan.Zero, receiver, "set", ActorRefs.NoSender);
            });
        }
    }
}

