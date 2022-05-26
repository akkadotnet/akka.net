// //-----------------------------------------------------------------------
// // <copyright file="Bugfix5962Spec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;
using static FluentAssertions.FluentActions;

namespace Akka.Cluster.Tests.SBR
{
    public class Bugfix5962Spec : TestKit.Xunit2.TestKit
    {
        private static readonly Config Config = ConfigurationFactory.ParseString(@"
akka {
    loglevel = INFO
    actor {
        provider = cluster
        default-dispatcher = {
            executor = channel-executor
            channel-executor.priority = normal
        }
        # Adding this part in combination with the SplitBrainResolverProvider causes the error
        internal-dispatcher = {
            executor = channel-executor
            channel-executor.priority = high
        }
    }
    remote {
        dot-netty.tcp {
            port = 15508
            hostname = ""127.0.0.1""
        }
        default-remote-dispatcher {
            executor = channel-executor
            channel-executor.priority = high
        }
        backoff-remote-dispatcher {
            executor = channel-executor
            channel-executor.priority = low
        }
    }
    cluster {
        seed-nodes = [""akka.tcp://Bugfix5962Spec@127.0.0.1:15508""]
        downing-provider-class = ""Akka.Cluster.SBR.SplitBrainResolverProvider, Akka.Cluster""
    }
}");

        private readonly Type _timerMsgType;
        
        public Bugfix5962Spec(ITestOutputHelper output): base(Config, nameof(Bugfix5962Spec), output)
        {
            _timerMsgType = Type.GetType("Akka.Actor.Scheduler.TimerScheduler+TimerMsg, Akka");
        }

        [Fact]
        public async Task SBR_Should_work_with_channel_executor()
        {
            var latch = new TestLatch(1);
            var cluster = Cluster.Get(Sys);
            cluster.RegisterOnMemberUp(() =>
            {
                latch.CountDown();
            });

            var selection = Sys.ActorSelection("akka://Bugfix5962Spec/system/cluster/core/daemon/downingProvider");
            
            await Awaiting(() => selection.ResolveOne(1.Seconds()))
                .Should().NotThrowAsync("Downing provider should be alive. ActorSelection will throw an ActorNotFoundException if this fails");
            
            // There should be no TimerMsg being sent to dead letter, this signals that the downing provider is dead
            await EventFilter.DeadLetter(_timerMsgType).ExpectAsync(0, async () =>
            {
                latch.Ready(1.Seconds());
                await Task.Delay(2.Seconds());
            });
        }
    }
}