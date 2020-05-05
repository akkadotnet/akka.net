//-----------------------------------------------------------------------
// <copyright file="Bug2640Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Routing;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Actor.Dispatch
{
    /// <summary>
    ///     Bugfix and verification spec for https://github.com/akkadotnet/akka.net/issues/2640
    ///     Used to verify that the <see cref="ForkJoinExecutor" /> gets properly disposed upon shutdown.
    /// </summary>
    public class Bug2640Spec : AkkaSpec
    {
        public Bug2640Spec(ITestOutputHelper helper) : base(DispatcherConfig, helper)
        {
        }

        private class GetThread
        {
            public static readonly GetThread Instance = new GetThread();

            private GetThread()
            {
            }
        }

        /// <summary>
        ///     Used to pass back data on the current thread.
        /// </summary>
        public class ThreadReporterActor : ReceiveActor
        {
            public ThreadReporterActor()
            {
                Receive<GetThread>(_ => Sender.Tell(Thread.CurrentThread));
            }
        }

        public static readonly Config DispatcherConfig = @"
            myapp{
                my-pinned-dispatcher {
                    type = PinnedDispatcher
                }
                my-fork-join-dispatcher{
                    type = ForkJoinDispatcher
                    throughput = 60
                    dedicated-thread-pool.thread-count = 4
                }
            }
        ";

        [Fact(DisplayName = "ForkJoinExecutor should terminate all threads upon ActorSystem.Terminate")]
        public async Task ForkJoinExecutorShouldShutdownUponActorSystemTermination()
        {
            var actor = Sys.ActorOf(Props.Create(() => new ThreadReporterActor())
                .WithDispatcher("myapp.my-fork-join-dispatcher").WithRouter(new RoundRobinPool(4)));

            Dictionary<int, Thread> threads = null;
            Watch(actor);
            for (var i = 0; i < 100; i++)
                actor.Tell(GetThread.Instance);

            threads = ReceiveN(100).Cast<Thread>().GroupBy(x => x.ManagedThreadId)
                .ToDictionary(x => x.Key, grouping => grouping.First());

            await Sys.Terminate();
            AwaitAssert(() =>
                threads.Values.All(x => x.IsAlive == false).Should().BeTrue("All threads should be stopped"));
        }

        [Fact(DisplayName = "ForkJoinExecutor should terminate all threads upon all attached actors shutting down")]
        public void ForkJoinExecutorShouldShutdownUponAllActorsTerminating()
        {
            var actor = Sys.ActorOf(Props.Create(() => new ThreadReporterActor())
                .WithDispatcher("myapp.my-fork-join-dispatcher").WithRouter(new RoundRobinPool(4)));

            Dictionary<int, Thread> threads = null;
            Watch(actor);
            var msgCount = 100;
            for (var i = 0; i < msgCount; i++)
                actor.Tell(GetThread.Instance);

            threads = ReceiveN(msgCount).Cast<Thread>().GroupBy(x => x.ManagedThreadId)
                .ToDictionary(x => x.Key, grouping => grouping.First());

            Sys.Stop(actor);
            ExpectTerminated(actor);
            AwaitAssert(() =>
                threads.Values.All(x => x.IsAlive == false).Should().BeTrue("All threads should be stopped"));
        }

        [Fact(DisplayName = "PinnedDispatcher should terminate its thread upon actor shutdown")]
        public void PinnedDispatcherShouldShutdownUponActorTermination()
        {
            var actor = Sys.ActorOf(Props.Create(() => new ThreadReporterActor())
                .WithDispatcher("myapp.my-pinned-dispatcher"));

            Watch(actor);
            actor.Tell(GetThread.Instance);
            var thread = ExpectMsg<Thread>();
            thread.IsAlive.Should().BeTrue();

            Sys.Stop(actor);
            ExpectTerminated(actor);
            AwaitCondition(() => !thread.IsAlive); // wait for thread to terminate
        }
    }
}
