//-----------------------------------------------------------------------
// <copyright file="FastLazySpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Util;
using Xunit;

namespace Akka.Tests.Util;

public class FastLazySpecs
{
    [Fact]
    public void FastLazy_should_indicate_no_value_has_been_produced()
    {
        var fal = new FastLazy<int>(() => 2);
        Assert.False(fal.IsValueCreated());
    }

    [Fact]
    public void FastLazy_should_produce_value()
    {
        var fal = new FastLazy<int>(() => 2);
        var value = fal.Value;
        Assert.Equal(2, value);
        Assert.True(fal.IsValueCreated());
    }

    [Fact]
    public void FastLazy_must_be_threadsafe()
    {
        for (var c = 0; c < 100000; c++) // try this 100000 times
        {
            var values = new ConcurrentBag<int>();
            var fal = new FastLazy<int>(() => new Random().Next(1, Int32.MaxValue));
            var result = Parallel.For(0, 1000, _ => values.Add(fal.Value)); // 1000 concurrent operations
            SpinWait.SpinUntil(() => result.IsCompleted);
            var value = values.First();
            Assert.NotEqual(0, value);
            Assert.True(values.All(x => x.Equals(value)));
        }
    }

    [Fact]
    public void FastLazy_only_single_value_creation_attempt()
    {
        int attempts = 0;
        Func<int> slowValueFactory = () =>
        {
            Interlocked.Increment(ref attempts);
            Thread.Sleep(100);
            return new Random().Next(1, Int32.MaxValue);
        };

        var values = new ConcurrentBag<int>();
        var fal = new FastLazy<int>(slowValueFactory);
        var result = Parallel.For(0, 1000, _ => values.Add(fal.Value)); // 1000 concurrent operations
        SpinWait.SpinUntil(() => result.IsCompleted);
        var value = values.First();
        Assert.NotEqual(0, value);
        Assert.True(values.All(x => x.Equals(value)));
        Assert.Equal(1000, values.Count);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void FastLazy_must_be_threadsafe_AnyRef()
    {
        for (var c = 0; c < 100000; c++) // try this 100000 times
        {
            var values = new ConcurrentBag<string>();
            var fal = new FastLazy<string>(() => Guid.NewGuid().ToString());
            var result = Parallel.For(0, 1000, _ => values.Add(fal.Value)); // 1000 concurrent operations
            SpinWait.SpinUntil(() => result.IsCompleted);
            var value = values.First();
            Assert.NotNull(value);
            Assert.True(values.All(x => x.Equals(value)));
        }
    }

    [Fact]
    public void FastLazy_only_single_value_creation_attempt_AnyRef()
    {
        int attempts = 0;
        Func<string> slowValueFactory = () =>
        {
            Interlocked.Increment(ref attempts);
            Thread.Sleep(100);
            return Guid.NewGuid().ToString();
        };

        var values = new ConcurrentBag<string>();
        var fal = new FastLazy<string>(slowValueFactory);
        var result = Parallel.For(0, 1000, _ => values.Add(fal.Value)); // 1000 concurrent operations
        SpinWait.SpinUntil(() => result.IsCompleted);
        var value = values.First();
        Assert.NotNull(value);
        Assert.True(values.All(x => x.Equals(value)));
        Assert.Equal(1000, values.Count);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void FastLazy_AllThreads_ShouldThrowException_WhenFactoryThrowsException()
    {
        var lazy = new FastLazy<string>(() => throw new Exception("Factory exception"));
        var result = Parallel.For(0, 10, i => { Assert.Throws<Exception>(() => _ = lazy.Value); });

        Assert.True(result.IsCompleted);
    }
}
