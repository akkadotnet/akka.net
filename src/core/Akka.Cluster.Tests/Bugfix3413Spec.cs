using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Tests
{
    /// <summary>
    /// Spec designed to reproduce https://github.com/akkadotnet/akka.net/issues/3413
    /// </summary>
    public class Bugfix3413Spec : AkkaSpec
    {
        const string Config = @"    
            akka.cluster {
              
            }
            akka.actor.provider = cluster
            akka.coordinated-shutdown.terminate-actor-system = on # want to keep this on - going to need it
            akka.remote.log-remote-lifecycle-events = off
            akka.remote.dot-netty.tcp.port = 0
            akka.coordinated-shutdown.phases.test-phase{
                depends-on = [cluster-shutdown]
                timeout = 5s
                recover = false
            }";

        public Bugfix3413Spec(ITestOutputHelper helper) : base(Config, helper)
        {
            _cluster = Cluster.Get(Sys);
        }

        private readonly Cluster _cluster;

        [Fact]
        public async Task CoordinatedShutdown_should_stop_ClusterDaemon()
        {
            // join cluster
            await _cluster.JoinAsync(_cluster.SelfAddress, new CancellationTokenSource(RemainingOrDefault).Token);

            // death watch the ClusterDaemon
            var clusterDaemon = await Sys.ActorSelection("/system/cluster").ResolveOne(RemainingOrDefault).ConfigureAwait(false);
            Watch(clusterDaemon);

            var tcs = new TaskCompletionSource<Done>();

            // actor system isn't going to terminate, but we should add a phase that executes after "cluster-shutdown"
            CoordinatedShutdown.Get(Sys).AddTask("test-phase", "test-task", () =>
            {
                tcs.TrySetResult(Done.Instance);
                Log.Info("Completing shutdown hook for test");
                return tcs.Task;
            });

           
            await _cluster.LeaveAsync(new CancellationTokenSource(RemainingOrDefault).Token);
            
            Within(TimeSpan.FromSeconds(10), () => {
                AwaitAssert(() => tcs.Task.IsCompleted.Should().BeTrue()); // coordinated shutdown should have run successfully
                ExpectTerminated(clusterDaemon);
            });
        }
    }
}
