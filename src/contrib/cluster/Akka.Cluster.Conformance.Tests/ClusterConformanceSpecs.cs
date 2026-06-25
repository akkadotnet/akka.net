//-----------------------------------------------------------------------
// <copyright file="ClusterConformanceSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Configuration;
using Xunit;

namespace Akka.Cluster.Conformance.Tests
{
    /// <summary>
    /// End-to-end conformance tests. Each test stands up an instrumented <see cref="ReferenceSeed"/>
    /// and drives a completely stock <see cref="InProcessWorker"/> through the membership lifecycle,
    /// then asks the "stop and teach" <see cref="ConformanceChecker"/> for a verdict derived purely
    /// from what the reference node observed. The worker is never instrumented.
    /// </summary>
    public class ClusterConformanceSpecs
    {
        private readonly ITestOutputHelper _output;

        public ClusterConformanceSpecs(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact(DisplayName = "A stock worker should pass every cluster conformance step: connect, converge, gracefully leave, cleanly shut down")]
        public async Task Should_pass_full_conformance_when_worker_follows_the_protocol()
        {
            await using var seed = await ReferenceSeed.StartAsync("ConformanceClusterPositive");
            var worker = InProcessWorker.Start("ConformanceClusterPositive", seed.SeedNodeUri);

            try
            {
                // Connect + converge
                Assert.True(await worker.WaitUntilUpAsync(TimeSpan.FromSeconds(20)),
                    "the worker never reached the Up state");
                Assert.True(await seed.WaitForUpMembersAsync(2, TimeSpan.FromSeconds(20)),
                    "the cluster never converged to two Up members");

                // the cluster broadcast router should reach the worker's /user/echo routee
                Assert.True(
                    await WaitUntilAsync(() => seed.Trace.Has("RoutedReply", worker.Address), TimeSpan.FromSeconds(15)),
                    "the broadcast router never got a reply from the worker's routee");

                // let gossip settle briefly so the membership is stable before leaving
                await Task.Delay(1000);

                // Graceful leave
                worker.LeaveGracefully();
                Assert.True(await seed.WaitForRemovedAsync(worker.Address, TimeSpan.FromSeconds(25)),
                    "the worker was never removed from the cluster");

                // give the reference node a beat to record the final MemberRemoved transition
                await Task.Delay(500);

                var result = Act.Check(seed.Trace, worker.Address);

                _output.WriteLine(result.ToString());
                _output.WriteLine(string.Empty);
                _output.WriteLine("---- full captured protocol + membership trace ----");
                _output.WriteLine(seed.Trace.Render());

                // The teaching tool should certify a conforming worker.
                result.EnsurePassed();
                Assert.True(result.Passed);
                Assert.Equal(result.TotalSteps, result.StepsCleared);
            }
            finally
            {
                await worker.DisposeAsync();
            }
        }

        [Fact(DisplayName = "A worker that crashes instead of leaving should fail at the graceful-leave step with a teaching message")]
        public async Task Should_stop_and_teach_when_worker_crashes_instead_of_leaving_gracefully()
        {
            // Speed up unreachability detection so the crash is observed quickly.
            var fast = ConfigurationFactory.ParseString(@"
                akka.cluster.failure-detector.acceptable-heartbeat-pause = 3 s
                akka.cluster.split-brain-resolver.stable-after = 6 s
            ");

            await using var seed = await ReferenceSeed.StartAsync("ConformanceClusterNegative", extraConfig: fast);
            var worker = InProcessWorker.Start("ConformanceClusterNegative", seed.SeedNodeUri, simulateCrashOnStop: true);

            // The worker connects and converges correctly...
            Assert.True(await worker.WaitUntilUpAsync(TimeSpan.FromSeconds(20)),
                "the worker never reached the Up state");
            Assert.True(await seed.WaitForUpMembersAsync(2, TimeSpan.FromSeconds(20)),
                "the cluster never converged to two Up members");
            // ...and even serves the broadcast router (clearing every step up to the graceful leave)...
            Assert.True(
                await WaitUntilAsync(() => seed.Trace.Has("RoutedReply", worker.Address), TimeSpan.FromSeconds(15)),
                "the broadcast router never got a reply from the worker's routee");

            // ...then it crashes, never announcing a graceful leave.
            await worker.CrashAsync();

            // Wait until the reference node observes the crash signature (unreachability).
            Assert.True(
                await WaitUntilAsync(() => seed.Trace.Has("UnreachableMember", worker.Address), TimeSpan.FromSeconds(25)),
                "the reference node never observed the crashed worker as unreachable");

            var result = Act.Check(seed.Trace, worker.Address);

            _output.WriteLine(result.ToString());
            _output.WriteLine(string.Empty);
            _output.WriteLine("---- full captured protocol + membership trace ----");
            _output.WriteLine(seed.Trace.Render());

            // The teaching tool must stop at the first unmet obligation: the graceful leave.
            Assert.False(result.Passed);
            Assert.Equal("Graceful leave announced (Leaving)", result.FailedStep);

            // It should have certified the earlier phases (connect, converge, broadcast) before stopping.
            Assert.True(result.StepsCleared >= 6,
                $"expected the connect/converge/broadcast steps to be cleared, but only {result.StepsCleared} were");

            // The teaching message must be protocol-level and explain the crash, language-agnostically.
            Assert.Contains("did not leave the cluster gracefully", result.Message);
            Assert.Contains("UNREACHABLE", result.Message);
            Assert.Contains("mark itself Leaving", result.Message);
        }

        private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return true;
                await Task.Delay(200);
            }

            return false;
        }
    }
}
