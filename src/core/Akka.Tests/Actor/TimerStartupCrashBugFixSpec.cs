//-----------------------------------------------------------------------
// <copyright file="TimerStartupCrashBugFixSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Routing;
using Akka.TestKit;
using FluentAssertions;
using FsCheck;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Actor;

public class TimerStartupCrashBugFixSpec : AkkaSpec
{
    public TimerStartupCrashBugFixSpec(ITestOutputHelper output) : base(output: output, Akka.Configuration.Config.Empty)
    {
        Sys.Log.Info("Starting TimerStartupCrashBugFixSpec");
    }

    private class TimerActor : UntypedActor, IWithTimers
    {
        public sealed class Check
        {
            public static Check Instance { get; } = new Check();

            private Check()
            {
            }
        }

        public sealed class Hit
        {
            public static Hit Instance { get; } = new Hit();

            private Hit()
            {
            }
        }

        private readonly ILoggingAdapter _log = Context.GetLogger();
        private int _counter = 0;
        public ITimerScheduler? Timers { get; set; } = null;

        protected override void PreStart()
        {
            Timers?.StartPeriodicTimer("key", Hit.Instance, TimeSpan.FromMilliseconds(1));
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Check _:
                    _log.Info("Check received");
                    Sender.Tell(_counter);
                    break;
                case Hit _:
                    _log.Info("Hit received");
                    _counter++;
                    break;
            }
        }

        protected override void PreRestart(Exception reason, object message)
        {
            _log.Error(reason, "Not restarting - shutting down");
            Context.Stop(Self);
        }
    }

    [Fact]
    public async Task TimerActor_should_not_crash_on_startup()
    {
        var actors = Enumerable.Range(0, 10).Select(c => Sys.ActorOf(Props.Create(() => new TimerActor()))).ToList();
        var watchTasks = actors.Select(actor => actor.WatchAsync()).ToList();

        var i = 0;
        while (i == 0)
        {
            // guarantee that the actor has started and processed a message from scheduler
            i = await actors[0].Ask<int>(TimerActor.Check.Instance);
        }


        watchTasks.Any(c => c.IsCompleted).Should().BeFalse();
    }
}
