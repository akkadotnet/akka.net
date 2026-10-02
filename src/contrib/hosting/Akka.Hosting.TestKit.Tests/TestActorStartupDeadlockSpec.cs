using Akka.TestKit;
using Microsoft.Extensions.Logging;

namespace Akka.Hosting.TestKit.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Actor;
using Hosting;
using Akka.Hosting.TestKit;
using Xunit;

file sealed class StartupPinger(IRequiredActor<TestProbe> testActorReq) : ReceiveActor
{
    protected override void PreStart()
    {
        // TestProbe is registered before user WithActors callbacks execute, so we can
        // use the synchronous ActorRef here and avoid blocking actor startup.
        testActorReq.ActorRef.Tell("startup-ping");
    }
}

file sealed class HostedTestKitRunner : TestKit, IAsyncLifetime  // TestKit already implements IAsyncLifetime, but we expose it here for clarity
{
    public HostedTestKitRunner(XunitTestOutputHelper output) 
        : base($"{Guid.NewGuid():N}", startupTimeout: TimeSpan.FromSeconds(20), output: output, logLevel: LogLevel.Error)
    {
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        // WithActors runs during ActorSystem creation
        // TestProbe should already be registered by the first startup hook
        builder.WithActors((system, _, resolver) =>
        {
            system.ActorOf(resolver.Props<StartupPinger>(), "pinger");
        });
    }

    // Optional convenience wrappers so the test code reads cleanly
    public Task StartAsync() => InitializeAsync().ToTask();
    public Task StopAsync()  => DisposeAsync().ToTask();

    public Task ExpectStartupAsync(TimeSpan? timeout = null)
        => ExpectMsgAsync("startup-ping", timeout ?? TimeSpan.FromSeconds(5)).AsTask();
}

public class TestActorStartupDeadlockSpec
{
    private readonly XunitTestOutputHelper _output;

    public TestActorStartupDeadlockSpec(XunitTestOutputHelper output)
    {
        _output = output;
    }
    
    // Give the overall test plenty of time; we enforce our own short, per-step timeouts below.
    [Fact(Timeout = 30000)] // Increased timeout for 30 parallel hosts
    public async Task parallel_host_start_should_not_deadlock()
    {
        // Per-runner bounded timeouts
        var startTimeout   = TimeSpan.FromSeconds(7); // pre-fix should trip this quickly
        var expectTimeout  = TimeSpan.FromSeconds(10); // Increased timeout for high concurrency
        var stopTimeout    = TimeSpan.FromSeconds(5);
        var concurrentHosts = 30;

        // Spin up N independent hosts concurrently inside the same theory
        var runners = Enumerable.Range(0, concurrentHosts)
                                .Select(_ => RunOneAsync())
                                .ToArray();

        await Task.WhenAll(runners);
        return;

        async Task RunOneAsync()
        {
            var id = Guid.NewGuid().ToString("N").Substring(0, 8);
            _output.WriteLine($"[{id}] Starting runner");
            var kit = new HostedTestKitRunner(_output);

            // --- START (bounded) ---
            _output.WriteLine($"[{id}] Calling StartAsync");
            var startTask = kit.StartAsync();
            var startDone = await Task.WhenAny(startTask, Task.Delay(startTimeout));
            if (startDone != startTask)
            {
                _output.WriteLine($"[{id}] StartAsync timed out after {startTimeout}");
                // Fail fast with a clear message rather than timing out the entire test method
                Assert.Fail(
                    $"Host did not start within {startTimeout}. " +
                    "This indicates the known startup deadlock (TestKit initialized inline on the startup thread).");
            }
            _output.WriteLine($"[{id}] StartAsync completed");
            // propagate any exception from StartAsync (e.g., watchdog TimeoutException)
            try
            {
                await startTask;
            }
            catch (Exception ex)
            {
                if (ex.Message.StartsWith("Timeout waiting for test actor"))
                    _output.WriteLine($"Original issue detected: {ex.Message}");
                
                throw;
            }

            try
            {
                // --- EXPECT (bounded) ---
                _output.WriteLine($"[{id}] Expecting startup ping");
                var expectTask = kit.ExpectStartupAsync(expectTimeout);
                var expectDone = await Task.WhenAny(expectTask, Task.Delay(expectTimeout));
                if (expectDone != expectTask)
                {
                    _output.WriteLine($"[{id}] ExpectStartupAsync timed out after {expectTimeout}");
                    Assert.Fail($"Did not receive startup ping within {expectTimeout}.");
                }
                _output.WriteLine($"[{id}] Received startup ping");
                await expectTask;
            }
            finally
            {
                // --- STOP (bounded) ---
                var stopTask = kit.StopAsync();
                var stopDone = await Task.WhenAny(stopTask, Task.Delay(stopTimeout));
                if (stopDone == stopTask)
                    await stopTask;
                // else: swallow to avoid hanging the test process on shutdown in pre-fix scenarios
            }
        }
    }
}
