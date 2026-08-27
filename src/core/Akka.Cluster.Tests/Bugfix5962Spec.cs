//-----------------------------------------------------------------------
// <copyright file="Bugfix5962Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions.Extensions;
using Xunit;


namespace Akka.Cluster.Tests
{
    public class Bugfix5962Spec : TestKit.Xunit.TestKit
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
            # A dynamic port avoids bind collisions with other suites/processes on shared CI agents.
            # The node self-joins programmatically (Cluster.Join(SelfAddress)) instead of using
            # static seed-nodes, which would require a port known ahead of time.
            port = 0
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
        downing-provider-class = ""Akka.Cluster.SBR.SplitBrainResolverProvider, Akka.Cluster""
    }
}");

        private readonly Type _timerMsgType;

        public Bugfix5962Spec(ITestOutputHelper output) : base(Config, nameof(Bugfix5962Spec), output)
        {
            _timerMsgType = Type.GetType("Akka.Actor.Scheduler.TimerScheduler+TimerMsg, Akka");
        }

        [Fact]
        public async Task SBR_Should_work_with_channel_executor()
        {
            // RunContinuationsAsynchronously: the RegisterOnMemberUp callback fires on the
            // OnMemberStatusChangedListener actor's dispatcher (a channel-executor/ThreadPool
            // thread) - this keeps the awaiting test continuation from running inline on that
            // dispatcher thread inside the actor's message processing.
            var memberUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cluster = Cluster.Get(Sys);
            cluster.RegisterOnMemberUp(() =>
            {
                memberUp.TrySetResult();
            });

            // Self-join programmatically - the downingProvider actor is created in
            // ClusterCoreDaemon.PreStart regardless of the join mechanism, so the regression
            // signal does not depend on seed-node joining (which would need a fixed port).
            cluster.Join(cluster.SelfAddress);

            var selection = Sys.ActorSelection("akka://Bugfix5962Spec/system/cluster/core/daemon/downingProvider");

            // Cluster extension startup is fire-and-forget: the downingProvider actor is created
            // asynchronously (ClusterDaemon -> ClusterCoreSupervisor -> ClusterCoreDaemon.PreStart)
            // after Cluster.Get() returns, so retry the resolution instead of a single 1s attempt.
            // If the downing provider died on startup (the original #5962 bug), resolution keeps
            // failing with ActorNotFoundException and this times out.
            await AwaitAssertAsync(
                () => selection.ResolveOne(1.Seconds()),
                duration: TimeSpan.FromSeconds(10));

            // Becoming Up requires the first leader-actions tick, which fires no earlier than
            // akka.cluster.periodic-tasks-initial-delay (1s) after cluster startup - a hard 1s wait
            // here is a razor-thin margin on a loaded CI agent, so use a generous dilated timeout.
            await memberUp.Task.WaitAsync(Dilated(TimeSpan.FromSeconds(30)));

            // There should be no TimerMsg being sent to dead letters - that would signal that the
            // downing provider is dead (the original #5962 regression). The SBR resolver ticks
            // every second, so a 2-second quiet window is guaranteed to observe dead-lettered
            // timer messages from a dead resolver. The explicit-timeout overload is used instead
            // of a raw Task.Delay: the TestKit dilates the timeout and keeps watching for matches
            // for the full window after the (no-op) action completes.
            await EventFilter.DeadLetter(_timerMsgType).ExpectAsync(
                expectedCount: 0,
                timeout: TimeSpan.FromSeconds(2),
                action: () => Task.CompletedTask);
        }
    }
}
