// -----------------------------------------------------------------------
//  <copyright file="MyActor.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

namespace Akka.Tests;

using System;
using Akka.Actor;

public sealed class MyActor : ReceiveActor, IWithTimers
{
    private sealed class TimerKey
    {
        public static readonly TimerKey Instance = new();
        private TimerKey() { }
    }
    
    private sealed class TimerMessage
    {
        public static readonly TimerMessage Instance = new();
        private TimerMessage() { }
    }

    public MyActor()
    {
        Receive<TimerMessage>(_ =>
        {
            // Timer callback code
        });
    }
    
    public ITimerScheduler Timers { get; set; }
    
    protected override void PostRestart(Exception reason)
    {
        base.PostRestart(reason);
        Timers.StartSingleTimer(
            key: TimerKey.Instance, 
            msg: TimerMessage.Instance, 
            timeout: TimeSpan.FromSeconds(3));
    }
}