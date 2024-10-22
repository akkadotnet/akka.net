//-----------------------------------------------------------------------
// <copyright file="TimerSchedulerDebugSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;
using Debug = Akka.Event.Debug;

namespace Akka.Tests.Actor.Scheduler;

internal sealed class TimerTestActor: UntypedActor, IWithTimers
{
    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case "startTimer":
                Timers.StartSingleTimer("test", "test", 1.Seconds());
                break;
            case "test":
                break;
            default:
                Unhandled(message);
                break;
        }
    }

    public ITimerScheduler Timers { get; set; }
}
    
public class TimerSchedulerDebug: TestKit.Xunit2.TestKit
{
    public TimerSchedulerDebug(ITestOutputHelper output) : base("akka.actor.debug.log-timers = true", null, output)
    {
    }

    [Fact]
    public void ShouldHonorDebugFlag()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(Debug));
        var timerActor = Sys.ActorOf<TimerTestActor>();
        timerActor.Tell("startTimer");

        FishForMessage(msg => msg is Debug dbg && dbg.Message.ToString()!.StartsWith("Start timer ["));
    }
}

public class TimerSchedulerSuppressDebug: TestKit.Xunit2.TestKit
{
    public TimerSchedulerSuppressDebug(ITestOutputHelper output) : base("akka.actor.debug.log-timers = false", null, output)
    {
    }

    [Fact]
    public async Task ShouldHonorSuppressDebugFlag()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(Debug));
        var timerActor = Sys.ActorOf<TimerTestActor>();
        timerActor.Tell("startTimer");

        await ExpectNoMsgAsync(
            predicate: msg => msg is Debug dbg && dbg.Message.ToString()!.StartsWith("Start timer ["), 
            timeout: 1.Seconds());
    }
    
    private async Task ExpectNoMsgAsync(
        Predicate<object> predicate, 
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.Now + (timeout ?? 3.Seconds());
        var offset = 0.Seconds();

        while (DateTime.Now < deadline)
        {
            var stopwatch = Stopwatch.StartNew();
            var (success, envelope) = await TryReceiveOneAsync(timeout - offset, cancellationToken);
            stopwatch.Stop();
            offset += stopwatch.Elapsed;
            
            if(!success)
                continue;

            var message = envelope.Message;
            if (!predicate(message)) 
                continue;
            
            Assertions.Fail("Expected no message to match predicate, received {0} instead.", message);
            break;
        }
    }
    
}
