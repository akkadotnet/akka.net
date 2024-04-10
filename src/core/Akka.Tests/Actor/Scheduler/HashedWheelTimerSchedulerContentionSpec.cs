// -----------------------------------------------------------------------
//  <copyright file="SchedulerHeavyUse.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Actor.Scheduler;

public class HashedWheelTimerSchedulerContentionSpec: TestKit.Xunit2.TestKit
{
    private const int TotalActor = 5000;
    private const int TotalThreads = 10;
    private const int ActorsPerThread = TotalActor / TotalThreads;
    
    public HashedWheelTimerSchedulerContentionSpec(ITestOutputHelper output) : base("{}", output)
    {
    }

    [Fact]
    public void SchedulerContentionTest()
    {
        var collector = CreateTestProbe();
        foreach (var i in Enumerable.Range(0, TotalActor))
        {
            Sys.ActorOf(Props.Create(() => new DoStuffActor(TestActor, collector)), i.ToString());
        }

        Within(10.Seconds(), () =>
        {
            for (var x = 0; x < TotalActor; x++)
            {
                ExpectMsg<Done>();
            }
        });

        object? received = null;
        do
        {
            received = collector.ReceiveOne(TimeSpan.Zero);
            if (received is long value)
            {
                value.Should().BeLessThan(200, "Scheduler should not experience resource contention");
            }
        } while (received is not null);
        
    }
    
    [Fact]
    public void SchedulerContentionThreadedTest()
    {
        var collector = CreateTestProbe();
        var threads = new List<Thread>();
        
        foreach (var j in Enumerable.Range(0, TotalThreads))
        {
            threads.Add(new Thread(() => RunThread(j)));
        }

        foreach (var thread in threads)
        {
            thread.Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        Within(10.Seconds(), () =>
        {
            for (var x = 0; x < TotalActor; x++)
            {
                ExpectMsg<Done>();
            }
        });

        object? received = null;
        do
        {
            received = collector.ReceiveOne(TimeSpan.Zero);
            if (received is long value)
            {
                value.Should().BeLessThan(200, "Scheduler should not experience resource contention");
            }
        } while (received is not null);

        return;

        void RunThread(int n)
        {
            n *= ActorsPerThread;
            for (var i = 0; i < ActorsPerThread; i++)
            {
                Sys.ActorOf(Props.Create(() => new DoStuffActor(TestActor, collector)), (n + i).ToString());
            }
        }
    }
    
    public class DoStuffActor : ReceiveActor, IWithTimers
    {
        private readonly IActorRef _collector;
        public ITimerScheduler Timers { get; set; }

        public DoStuffActor(IActorRef probe, IActorRef collector)
        {
            _collector = collector;
            
            Receive<Done>(d =>
            {
                Context.Stop(Self);
                probe.Tell(d);
            });
        }

        protected override void PreStart()
        {
            base.PreStart();
            var sw = Stopwatch.StartNew();
            Timers.StartSingleTimer("Test", Done.Instance, TimeSpan.FromSeconds(3));
            sw.Stop();

            if (sw.ElapsedMilliseconds > 0)
            {
                Context.GetLogger().Info($"{sw.ElapsedMilliseconds}");
                _collector.Tell(sw.ElapsedMilliseconds);
            }
        }
    }
}