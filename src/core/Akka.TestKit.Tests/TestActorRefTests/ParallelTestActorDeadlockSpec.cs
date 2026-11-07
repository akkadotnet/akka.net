//-----------------------------------------------------------------------
// <copyright file="ParallelTestActorDeadlockSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Xunit;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    /// <summary>
    /// Reproduces the deadlock that used to hit Akka.Hosting.TestKit when many TestKits start at
    /// once and an actor talks to the TestActor from its own <c>PreStart</c>.
    /// <para>
    /// Related issues:
    /// <list type="bullet">
    /// <item>https://github.com/akkadotnet/akka.net/issues/7770</item>
    /// <item>https://github.com/akkadotnet/Akka.Hosting/pull/643</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class ParallelTestActorDeadlockSpec
    {
        private const int ConcurrentTestKits = 16;

        private readonly ITestOutputHelper _output;

        public ParallelTestActorDeadlockSpec(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact(Timeout = 20000)]
        public async Task Parallel_TestKit_startup_should_not_deadlock()
        {
            // Task.Run is deliberate: the bodies must run on ThreadPool threads. That is what
            // Akka.Hosting.TestKit does - it builds the TestKit inside a StartActors callback on a
            // host-startup continuation - and it is the condition this spec exists to guard.
            // Building a TestKit blocks its caller (LoggingBus.StartDefaultLoggers waits for the
            // loggers, TestKitBase waits for the TestActor's PreStart), and every one of those waits
            // is for work that runs on the same pool. If construction ever waits on something that
            // can only make progress on the blocked thread, this is where it shows up.
            //
            // The same shape also makes the spec sensitive to plain pool starvation on small CI
            // agents. Measured with sixteen blocked pool threads on two cores: construction takes up
            // to 5.3s (the loggers hit akka.logger-startup-timeout on the starved pool), and the
            // PingerActor can take up to 3.1s to exist after ActorOf returns. The per-step budgets
            // below are sized for that and dilated so akka.test.timefactor applies; the [Fact]
            // timeout is the actual deadlock detector, and construction plus one wait fits under it.
            var tasks = Enumerable.Range(0, ConcurrentTestKits)
                .Select(_ => Task.Run(RunOneTestKit))
                .ToArray();

            await Task.WhenAll(tasks);

            async Task RunOneTestKit()
            {
                var id = Guid.NewGuid().ToString("N").Substring(0, 8);
                var sw = Stopwatch.StartNew();
                try
                {
                    // Config.Empty + actorSystemName. The single-string overload parses its argument
                    // as HOCON, so passing the id there named every system "test" and left the log
                    // output unattributable.
                    await using var testKit =
                        new Akka.TestKit.Xunit.TestKit(Config.Empty, $"test-{id}", _output);
                    var created = sw.Elapsed;

                    // Simulates what Akka.Hosting does: an actor created during startup that talks
                    // to the TestActor from PreStart.
                    var pinger = testKit.Sys.ActorOf(Props.Create(() => new PingerActor(testKit.TestActor)));

                    // ActorOf hands back a RepointableActorRef - the actor does not exist yet and
                    // PreStart has not run. Wait for it to become real instead of assuming the ping
                    // is already on its way.
                    await testKit.Sys.ActorSelection(pinger.Path)
                        .ResolveOne(testKit.Dilated(TimeSpan.FromSeconds(10)));
                    var started = sw.Elapsed;

                    await testKit.ExpectMsgAsync<string>("ping", testKit.Dilated(TimeSpan.FromSeconds(10)));
                    var pinged = sw.Elapsed;

                    // ...and the TestKit still works normally afterwards.
                    testKit.TestActor.Tell("test-message");
                    await testKit.ExpectMsgAsync<string>("test-message", testKit.Dilated(TimeSpan.FromSeconds(10)));

                    _output.WriteLine(
                        $"[{id}] created={created.TotalMilliseconds:F0}ms " +
                        $"pinger-started=+{(started - created).TotalMilliseconds:F0}ms " +
                        $"ping=+{(pinged - started).TotalMilliseconds:F0}ms " +
                        $"total={sw.Elapsed.TotalMilliseconds:F0}ms");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"[{id}] failed at {sw.Elapsed.TotalMilliseconds:F0}ms: {ex.Message}");
                    throw;
                }
            }
        }

        private sealed class PingerActor : ActorBase
        {
            private readonly IActorRef _testActor;

            public PingerActor(IActorRef testActor)
            {
                _testActor = testActor;
            }

            protected override bool Receive(object message) => false;

            protected override void PreStart() => _testActor.Tell("ping");
        }
    }
}
