using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Xunit;
using Xunit.Abstractions;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class ParallelTestActorDeadlockSpec
    {
        private readonly ITestOutputHelper _output;

        public ParallelTestActorDeadlockSpec(ITestOutputHelper output)
        {
            _output = output;
        }

        // This test reproduces the deadlock that occurs in Akka.Hosting.TestKit
        // when multiple TestKits start up in parallel and actors try to interact
        // with TestActor during initialization.
        // 
        // Related issues:
        // - https://github.com/akkadotnet/akka.net/issues/7770
        // - https://github.com/akkadotnet/Akka.Hosting/pull/643
        [Fact(Timeout = 20000)]
        public async Task Parallel_TestKit_startup_should_not_deadlock()
        {
            var concurrentTests = 40; // High parallelism to trigger the issue

            var tasks = Enumerable.Range(0, concurrentTests)
                .Select(_ => Task.Run(RunOneTestKit))
                .ToArray();

            await Task.WhenAll(tasks);

            async Task RunOneTestKit()
            {
                // Removed inner Task.Run - it was causing unnecessary thread pool queueing
                // and increasing the likelihood of scheduling delays
                var id = Guid.NewGuid().ToString("N").Substring(0, 8);
                try
                {
                    _output.WriteLine($"[{id}] Creating TestKit...");
                    // Create TestKit synchronously like a normal test would
                    using var testKit = new Akka.TestKit.Xunit2.TestKit($"test-{id}", output: _output);
                    _output.WriteLine($"[{id}] TestKit created");

                    // Simulate what happens in Akka.Hosting - actor creation during startup
                    // that tries to interact with TestActor
                    _output.WriteLine($"[{id}] Creating PingerActor...");
                    var actor = testKit.Sys.ActorOf(Props.Create(() => new PingerActor(testKit.TestActor)));
                    _output.WriteLine($"[{id}] PingerActor created");

                    // Increased timeout from 2s to 5s to account for thread pool delays under high parallelism
                    // Under heavy load (40 concurrent tests), PreStart() execution can be delayed due to
                    // thread pool starvation, and timer drift can cause the timeout to fire late
                    await testKit.ExpectMsgAsync<string>("ping", TimeSpan.FromSeconds(5));
                    _output.WriteLine($"[{id}] Received ping from PingerActor");

                    // Now verify the TestKit is working normally
                    _output.WriteLine($"[{id}] Sending test message...");
                    testKit.TestActor.Tell("test-message");
                    await testKit.ExpectMsgAsync<string>("test-message", TimeSpan.FromSeconds(5));
                    _output.WriteLine($"[{id}] Test completed successfully");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"[{id}] Failed: {ex.Message}");
                    throw;
                }
            }
        }

        private class PingerActor : ActorBase
        {
            private readonly IActorRef _testActor;

            public PingerActor(IActorRef testActor)
            {
                _testActor = testActor;
            }

            protected override bool Receive(object message) => false;

            protected override void PreStart()
            {
                // This simulates what StartupPinger does in Akka.Hosting
                // Sending a message to TestActor during actor initialization
                _testActor.Tell("ping");
            }
        }
    }
}