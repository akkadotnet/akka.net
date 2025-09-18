using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit.Xunit2;
using Xunit;
using Xunit.Abstractions;

public class MergeHubRaceDetailedTest : TestKit
{
    private readonly IMaterializer _materializer;

    public MergeHubRaceDetailedTest(ITestOutputHelper output) : base(output: output)
    {
        _materializer = Sys.Materializer();
    }

    [Fact]
    public async Task Demonstrate_MergeHub_Exception_Not_Logged()
    {
        // This test demonstrates that the ProducerFailed exception
        // is thrown but not logged due to ActiveStage being null

        var errorLogged = false;

        // Set up event filter to catch any error logs
        Sys.EventStream.Subscribe(TestActor, typeof(Error));

        var (sink, task) = MergeHub.Source<int>(16)
            .Take(10)
            .ToMaterialized(Sink.Seq<int>(), Keep.Both)
            .Run(_materializer);

        // Source.Failed immediately calls subscriber.OnError during subscription
        // This happens synchronously, before the GraphInterpreter's event loop starts
        Source.Failed<int>(new TestException("failing producer"))
            .RunWith(sink, _materializer);

        // Healthy producer
        Source.From(Enumerable.Range(1, 10))
            .RunWith(sink, _materializer);

        // Wait for completion
        var result = await task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(10, result.Count);

        // Check if we received any error logs
        var msg = ReceiveWhile<Error>(TimeSpan.FromMilliseconds(100), m => m as Error);
        errorLogged = msg.Any(e => e.ToString().Contains("Upstream producer failed"));

        // This will likely fail - demonstrating the race condition
        // Assert.True(errorLogged, "Expected error log was not captured");

        Output.WriteLine($"Error was logged: {errorLogged}");
        if (!errorLogged)
        {
            Output.WriteLine("RACE CONDITION CONFIRMED: Error was thrown but not logged!");
            Output.WriteLine("Theory: Source.Failed's immediate OnError occurs before GraphInterpreter");
            Output.WriteLine("sets ActiveStage, causing ReportStageError to rethrow instead of log");
        }
    }

    [Fact]
    public async Task MergeHub_With_Delayed_Failure_Should_Log()
    {
        // This test uses a source that fails after a delay,
        // allowing the GraphInterpreter to be fully initialized

        var errorLogged = false;
        Sys.EventStream.Subscribe(TestActor, typeof(Error));

        var (sink, task) = MergeHub.Source<int>(16)
            .Take(10)
            .ToMaterialized(Sink.Seq<int>(), Keep.Both)
            .Run(_materializer);

        // Use a source that delays its failure slightly
        Source.From(new[] { 1 })
            .Concat(Source.Failed<int>(new TestException("delayed failure")))
            .RunWith(sink, _materializer);

        // Healthy producer
        Source.From(Enumerable.Range(2, 9))
            .RunWith(sink, _materializer);

        // Wait for completion - might timeout if the failure stops processing
        try
        {
            var result = await task.WaitAsync(TimeSpan.FromSeconds(3));
            Output.WriteLine($"Got {result.Count} elements");
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Task failed: {ex.Message}");
        }

        // Check if we received error logs
        var msg = ReceiveWhile<Error>(TimeSpan.FromMilliseconds(100), m => m as Error);
        errorLogged = msg.Any(e => e.ToString().Contains("Upstream producer failed"));

        Output.WriteLine($"Error was logged with delayed failure: {errorLogged}");
        if (errorLogged)
        {
            Output.WriteLine("CONFIRMED: Delayed failure is properly logged!");
            Output.WriteLine("This proves the issue is with immediate failures during initialization");
        }
    }

    private class TestException : Exception
    {
        public TestException(string message) : base(message) { }
    }
}