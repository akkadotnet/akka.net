using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Actor.Dispatch
{
    /// <summary>
    /// Bugfix and verification spec for https://github.com/akkadotnet/akka.net/issues/2640
    ///
    /// Used to verify that the <see cref="ForkJoinExecutor"/> gets properly disposed upon shutdown.
    /// </summary>
    public class Bug2640Spec : AkkaSpec
    {
        class GetThread
        {
            public static readonly GetThread Instance = new GetThread();
            private GetThread() { }
        }

        /// <summary>
        /// Used to pass back data on the current thread.
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

        public Bug2640Spec(ITestOutputHelper helper) : base(DispatcherConfig, helper) { }

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

        [Fact(DisplayName = "ForkJoinExecutor should terminate all threads upon all attached actors shutting down")]
        public void ForkJoinExecutorShouldShutdownUponAllActorsTerminating()
        {

        }
    }
}
