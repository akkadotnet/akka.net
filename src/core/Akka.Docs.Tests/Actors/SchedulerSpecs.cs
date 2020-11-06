//-----------------------------------------------------------------------
// <copyright file="CoordinatedShutdownSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    }
}
