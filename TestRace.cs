using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit.Xunit2;
using Xunit;
using Xunit.Abstractions;
using Akka.Event;

public class MergeHubRaceTest : TestKit
{
    private readonly IMaterializer _materializer;

    public MergeHubRaceTest(ITestOutputHelper output) : base(output: output)
    {
        _materializer = Sys.Materializer();
    }

    [Fact]
    public async Task Test_MergeHub_Race_Condition()
    {
        // Run the test many times to try to catch the race
        for (int i = 0; i < 100; i++)
        {
            await TestOnce();
        }
    }

    private async Task TestOnce()
    {
        var (sink, task) = MergeHub.Source<int>(16)
            .Take(10)
            .ToMaterialized(Sink.Seq<int>(), Keep.Both)
            .Run(_materializer);

        // This is the race:
        // 1. Failing producer connects and immediately fails
        Source.Failed<int>(new TestException("failing")).RunWith(sink, _materializer);

        // 2. Healthy producer provides all 10 elements quickly
        Source.From(Enumerable.Range(1, 10)).RunWith(sink, _materializer);

        var result = await task.WaitAsync(TimeSpan.FromSeconds(3));

        // The test should complete regardless
        Assert.Equal(10, result.Count);
    }

    private class TestException : Exception
    {
        public TestException(string message) : base(message) { }
    }
}