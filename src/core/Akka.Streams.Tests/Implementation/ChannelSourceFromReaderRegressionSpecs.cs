//-----------------------------------------------------------------------
// <copyright file="ChannelSourceFromReaderRegressionSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Implementation;

// Simple reference type used in the reproducer (matches the Discord/GitHub thread)
public sealed class Message<TKey, TValue>
{
    public TKey Key { get; init; }
    public TValue Value { get; init; }
}

public sealed class ChannelSourceFromReaderRegressionSpecs : Akka.TestKit.Xunit2.TestKit
{
    private readonly IMaterializer _mat;

    public ChannelSourceFromReaderRegressionSpecs(ITestOutputHelper output) : base(output: output)
    {
        _mat = Sys.Materializer();
    }
    
    [Fact(DisplayName = "FromReader: closing without writing any elements should complete stream (no NRE)")]
    public async Task FromReader_should_complete_cleanly_with_zero_elements()
    {
        var ch = Channel.CreateBounded<Message<string, string>>(new BoundedChannelOptions(8)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        var src = ChannelSource.FromReader(ch.Reader);

        // Collect to a list to ensure materialized task completes on stage completion
        var resultTask = src.RunWith(Sink.Seq<Message<string, string>>(), _mat);

        // Complete the writer without sending any items (problematic path pre-fix)
        ch.Writer.Complete();

        var results = await resultTask.Within(TimeSpan.FromSeconds(5));
        Assert.Empty(results); // main assertion is actually "no exception"
    }

    [Fact(DisplayName = "FromReader: one element then close should complete stream (no NRE)")]
    public async Task FromReader_should_complete_cleanly_with_one_element_then_close()
    {
        var ch = Channel.CreateBounded<Message<string, string>>(new BoundedChannelOptions(8)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        var src = ChannelSource.FromReader(ch.Reader);
        var resultTask = src.RunWith(Sink.Seq<Message<string, string>>(), _mat);

        // Write a single reference-type element then complete
        ch.Writer.TryWrite(new Message<string, string> { Key = "k1", Value = "v1" });
        ch.Writer.Complete();

        var results = await resultTask.Within(TimeSpan.FromSeconds(5));
        Assert.Single(results);
        Assert.Equal("k1", results[0].Key);
        Assert.Equal("v1", results[0].Value);
    }

    [Fact(DisplayName = "FromReader: failure completion should fail the stream with the same exception")]
    public async Task FromReader_should_propagate_failure_instead_of_throwing_NRE()
    {
        var ch = Channel.CreateBounded<Message<string, string>>(new BoundedChannelOptions(8)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        var src = ChannelSource.FromReader(ch.Reader);

        // Materialize to Ignore; we only care that the materialized task faults with our exception
        var resultTask = src.RunWith(Sink.Ignore<Message<string, string>>(), _mat);

        var boom = new InvalidOperationException("boom");
        ch.Writer.TryComplete(boom);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await resultTask.Within(TimeSpan.FromSeconds(5));
        });
        Assert.Equal("boom", ex.Message);
    }

    [Fact(DisplayName = "FromReader: value type smoke test should not regress")]
    public async Task FromReader_should_work_with_value_types()
    {
        var ch = Channel.CreateBounded<int>(new BoundedChannelOptions(8)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        var src = ChannelSource.FromReader(ch.Reader);
        var resultTask = src.RunWith(Sink.Seq<int>(), _mat);

        ch.Writer.TryWrite(42);
        ch.Writer.Complete();

        var results = await resultTask.Within(TimeSpan.FromSeconds(5));
        Assert.Single(results);
        Assert.Equal(42, results[0]);
    }
}

internal static class TaskTimeoutExtensions
{
    /// <summary>
    /// Helper to await a Task with a timeout (throws if time is exceeded).
    /// </summary>
    public static async Task<T> Within<T>(this Task<T> task, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var completed = await Task.WhenAny(task, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token));
        if (completed != task)
            throw new TimeoutException($"Task did not complete within {timeout}.");
        return await task; // unwrap exceptions if any
    }

    public static async Task Within(this Task task, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var completed = await Task.WhenAny(task, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token));
        if (completed != task)
            throw new TimeoutException($"Task did not complete within {timeout}.");
        await task; // unwrap exceptions if any
    }
}