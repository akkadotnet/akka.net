//-----------------------------------------------------------------------
// <copyright file="SchedulerShutdownSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.Tests.Util;
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
            public readonly AtomicCounter Shutdown = new(0);

            

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

            protected override void InternalScheduleOnce(TimeSpan delay, IRunnable action, ICancelable cancelable)
            {
                action.Run();
            }

            protected override void InternalScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action, ICancelable cancelable)
            {
                action();
            }

            protected override void InternalScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, IRunnable action, ICancelable cancelable)
            {
                action.Run();
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
        public async Task ActorSystem_must_terminate_scheduler_on_shutdown()
        {
            ActorSystem sys = null;
            try
            {
                sys = ActorSystem.Create("SchedulerShutdownSys1", Config);
                var scheduler = (ShutdownScheduler)sys.Scheduler;
                var currentCounter = scheduler.Shutdown.Current;
                (await sys.Terminate().AwaitWithTimeout(sys.Settings.SchedulerShutdownTimeout)).Should().BeTrue();
                (scheduler.Shutdown.Current).Should().Be(currentCounter + 1);
            }
            finally
            {
                await sys?.Terminate().AwaitWithTimeout(TimeSpan.FromSeconds(5));
            }
        }

        [Fact]
        public async Task ActorSystem_must_terminate_scheduler_with_queued_work_on_shutdown()
        {
            ActorSystem sys = null;
            try
            {
                var i = 0;
                sys = ActorSystem.Create("SchedulerShutdownSys1", Config);
                var scheduler = (ShutdownScheduler)sys.Scheduler;
                sys.Scheduler.Advanced.ScheduleRepeatedly(TimeSpan.FromMilliseconds(0), TimeSpan.FromMilliseconds(10), () => i++);
                await Task.Delay(100); // give the scheduler a chance to start and run
                var currentCounter = scheduler.Shutdown.Current;
                (await sys.Terminate().AwaitWithTimeout(sys.Settings.SchedulerShutdownTimeout)).Should().BeTrue();
                (scheduler.Shutdown.Current).Should().Be(currentCounter + 1);
                var stoppedValue = i;
                stoppedValue.Should().BeGreaterThan(0, "should have incremented at least once");
                await Task.Delay(100);
                i.Should().Be(stoppedValue, "Scheduler shutdown; should not still be incrementing values.");
            }
            finally
            {
                sys?.Terminate().AwaitWithTimeout(TimeSpan.FromSeconds(5));
            }
        }

        [Fact]
        public async Task ActorSystem_default_scheduler_mustbe_able_to_terminate_on_shutdown()
        {
            ActorSystem sys = ActorSystem.Create("SchedulerShutdownSys2");
            Assert.True(sys.Scheduler is IDisposable);
            await sys.Terminate().AwaitWithTimeout(TimeSpan.FromSeconds(5));
        }


        private class MyScheduledActor : ReceiveActor
        {
            private bool _received = false;

            public MyScheduledActor()
            {
                Receive<string>(str => str.Equals("get"), _ =>
                {
                    Sender.Tell(_received);
                });

                Receive<string>(str => str.Equals("set"), _ =>
                {
                    _received = true;
                });
            }
        }

        [Fact]
        public async Task ActorSystem_default_scheduler_must_never_accept_more_work_after_shutdown()
        {
            ActorSystem sys = ActorSystem.Create("SchedulerShutdownSys3");
            var receiver = sys.ActorOf(Props.Create(() => new MyScheduledActor()));
            sys.Scheduler.ScheduleTellOnce(0, receiver, "set", ActorRefs.NoSender);
            await Task.Delay(50); // let the scheduler run
            var received = await receiver.Ask<bool>("get", TimeSpan.FromMilliseconds(100));
            Assert.True(received);

            var terminated = await sys.Terminate().AwaitWithTimeout(TimeSpan.FromSeconds(5));
            if (!terminated)
                Assert.True(false, "Expected ActorSystem to terminate within 5s. Took longer.");

            Assert.Throws<SchedulerException>(() =>
            {
                sys.Scheduler.ScheduleTellOnce(TimeSpan.Zero, receiver, "set", ActorRefs.NoSender);
            });
        }
    }
}

