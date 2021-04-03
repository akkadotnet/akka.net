//-----------------------------------------------------------------------
// <copyright file="SchedulerSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using Xunit;

namespace DocsExamples.Actors
{
    public class SchedulerSpecs : TestKit
    {
        public sealed class StopPrinting{}
        public sealed class CanPrint{}

        // <TimerActor>
        public sealed class Print { }
        public sealed class Total { }

        public sealed class TimerActor : ReceiveActor, IWithTimers
        {
            public ITimerScheduler Timers { get; set; }

            private int _count = 0;
            private ILoggingAdapter _log = Context.GetLogger();

            public TimerActor()
            {
                Receive<int>(i =>
                {
                    _count += i;
                });

                Receive<Print>(_ => _log.Info("Current count is [{0}]", _count));
                Receive<Total>(_ => Sender.Tell(_count));
            }

            protected override void PreStart()
            {
                // start two recurring timers
                // both timers will be automatically disposed when actor is stopped
                Timers.StartPeriodicTimer("print", new Print(), TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(5));
                Timers.StartPeriodicTimer("add", 1, TimeSpan.FromMilliseconds(0), TimeSpan.FromMilliseconds(20));
            }
        }
        // </TimerActor>

        public sealed class StartStopTimerActor : ReceiveActor, IWithTimers
        {
            public ITimerScheduler Timers { get; set; }

            private int _count = 0;
            private ILoggingAdapter _log = Context.GetLogger();

            public StartStopTimerActor()
            {
                Receive<int>(i =>
                {
                    _count += i;
                });

                Receive<StopPrinting>(_ => StopPrintTimer());
                Receive<CanPrint>(_ =>
                {
                    // <CheckTimer>
                    var isPrintTimerActive = Timers.IsTimerActive("print");
                    Sender.Tell(isPrintTimerActive);
                    // </CheckTimer>
                });
                Receive<Print>(_ => _log.Info("Current count is [{0}]", _count));
                Receive<Total>(_ => Sender.Tell(_count));
            }

            protected override void PreStart()
            {
                StartTimers();
            }

            private void StartTimers()
            {
                // <StartTimers>
                
                // start single timer that fires off 5 seconds in the future
                Timers.StartSingleTimer("print", new Print(), TimeSpan.FromSeconds(5));

                // start recurring timer
                Timers.StartPeriodicTimer("add", 1, TimeSpan.FromMilliseconds(0), TimeSpan.FromMilliseconds(20));
                // </StartTimers>
            }

            private void StopPrintTimer()
            {
                // <StopTimers>
                // cancels the print timer
                Timers.Cancel("print");

                // cancels all of the timers that belong to this actor
                // (also called automatically when this actor is stopped)
                Timers.CancelAll();
                // </StopTimers>
            }
        }

        // <Scheduler>
        public class SchedulerActor : ReceiveActor
        {
            private int _count = 0;
            private ILoggingAdapter _log = Context.GetLogger();

            private ICancelable _printTask;
            private ICancelable _addTask;

            public SchedulerActor()
            {
                Receive<int>(i =>
                {
                    _count += i;
                });

                Receive<Print>(_ => _log.Info("Current count is [{0}]", _count));
                Receive<Total>(_ => Sender.Tell(_count));
            }

            protected override void PreStart()
            {
                // start two recurring timers
                // both timers will be automatically disposed when actor is stopped
                _printTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(TimeSpan.FromSeconds(0.1),
                    TimeSpan.FromSeconds(5), Self, new Print(), ActorRefs.NoSender);

                _addTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(TimeSpan.FromMilliseconds(0), 
                    TimeSpan.FromMilliseconds(20), Self, 1, ActorRefs.NoSender);
            }

            protected override void PostStop()
            {
                // have to cancel all recurring scheduled tasks
                _printTask?.Cancel();
                _addTask?.Cancel();
            }
        }
        // </Scheduler>

        [Fact]
        public async Task TimerActorShouldIncrementOverTime()
        {
            var timerActor = Sys.ActorOf(Props.Create(() => new TimerActor()), "timers");

            timerActor.Tell(new Total());
            var count1 = ExpectMsg<int>();

            await Task.Delay(100); // pause for 100ms

            timerActor.Tell(new Total());
            var count2 = ExpectMsg<int>();

            count1.Should().BeLessThan(count2);
        }

        [Fact]
        public async Task TimerShouldStopCorrectly()
        {
            var timerActor = Sys.ActorOf(Props.Create(() => new StartStopTimerActor()), "timers");

            var canPrint1 = await timerActor.Ask<bool>(new CanPrint(), TimeSpan.FromSeconds(1));
            canPrint1.Should().BeTrue();

            timerActor.Tell(new StopPrinting());

            var canPrint2 = await timerActor.Ask<bool>(new CanPrint(), TimeSpan.FromSeconds(1));
            canPrint2.Should().BeFalse();
        }
    }
}
