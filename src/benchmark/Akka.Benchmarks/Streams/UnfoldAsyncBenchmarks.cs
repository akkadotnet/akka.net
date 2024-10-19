// //-----------------------------------------------------------------------
// // <copyright file="UnfoldAsyncBenchmarks.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Streams;
using Akka.Streams.Dsl;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Streams;

[Config(typeof(MicroBenchmarkConfig))]
public class UnfoldAsyncBenchmarks
{
    public struct IntOrCompletion
    {
        public readonly int IntValue;
        public readonly TaskCompletionSource? Completion;

        public IntOrCompletion(int intValue, TaskCompletionSource? completion)
        {
            IntValue = intValue;
            Completion = completion;
        }
    }
    private ActorSystem system;
    private ActorMaterializer materializer;

    private IRunnableGraph<Task> simpleGraph;
    private Task<Done> selectAsyncStub;
    private Channel<IntOrCompletion> asyncNoYieldCh;
    private Task<Done> selectValueTaskAsyncStub;
    private Channel<IntOrCompletion> vtAsyncCh;
    private Task<Done> unfoldAsyncSyncStub;
    private Task<Done> selectAsyncValueTaskSyncStub;
    private Channel<IntOrCompletion> asyncYieldCh;
    private Channel<IntOrCompletion> vtAsyncYieldCh;

    [GlobalSetup]
    public void Setup()
    {
        system = ActorSystem.Create("system");
        materializer = system.Materializer();
        asyncNoYieldCh = Channel.CreateUnbounded<IntOrCompletion>();
            
        asyncYieldCh = Channel.CreateUnbounded<IntOrCompletion>();
            
        vtAsyncYieldCh = Channel.CreateUnbounded<IntOrCompletion>();
            
        unfoldAsyncSyncStub = Source.UnfoldAsync<ChannelReader<IntOrCompletion>,int>(asyncYieldCh.Reader, async r =>
            {
                var i = await r.ReadAsync();
                if (i.Completion != null)
                {
                    i.Completion.TrySetResult();
                    return (r, -1);
                }
                else
                {
                    return (r, i.IntValue);
                }
            })
            .RunWith(Sink.Ignore<int>(), materializer);
            
        selectAsyncValueTaskSyncStub = Source.UnfoldValueTaskAsync<ChannelReader<IntOrCompletion>,int>(vtAsyncYieldCh.Reader, async r =>
            {
                var i = await r.ReadAsync();
                if (i.Completion != null)
                {
                    i.Completion.TrySetResult();
                    return (r, -1);
                }
                else
                {
                    return (r, i.IntValue);
                }
            })
            .RunWith(Sink.Ignore<int>(), materializer);
        selectAsyncStub = Source.UnfoldAsync<ChannelReader<IntOrCompletion>,int>(asyncNoYieldCh.Reader,async r =>
        { 
            await Task.Yield();
            var a = await r.ReadAsync(); 
            if (a.Completion != null)
            {
                a.Completion.TrySetResult();
                return (r, -1);
            }
            else
            {
                //await Task.Yield();
                //        await Task.Delay(0);
                return (r, a.IntValue);
            }
        }).RunWith(Sink.Ignore<int>(), materializer);
        vtAsyncCh = Channel.CreateUnbounded<IntOrCompletion>();
        int vta = 0;
        selectValueTaskAsyncStub = Source.UnfoldValueTaskAsync<ChannelReader<IntOrCompletion>,int>(vtAsyncCh.Reader,async r =>
        {
            await Task.Yield();
            var a = await r.ReadAsync();
            if (a.Completion != null)
            {
                a.Completion.TrySetResult();
                return (r, -1);
            }
            else
            {
                //await Task.Yield();
                //await Task.Delay(0);
                return (r, a.IntValue);
            }
        }).RunWith(Sink.Ignore<int>(), materializer);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        materializer.Dispose();
        system.Dispose();
    }
        
    [Benchmark]
    public async Task UnfoldAsyncYieldInConsume()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions
            .RunContinuationsAsynchronously);
        for (int i = 0; i < 100; i++)
        {
            asyncNoYieldCh.Writer.TryWrite(new IntOrCompletion(i, null));
        }

        asyncNoYieldCh.Writer.TryWrite(new IntOrCompletion(0, completion));
        await completion.Task;

    }
        
        
    [Benchmark]
    public async Task UnfoldValueTaskAsyncYieldInConsume()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions
            .RunContinuationsAsynchronously);
        for (int i = 0; i < 100; i++)
        {
            vtAsyncCh.Writer.TryWrite(new IntOrCompletion(i, null));
        }

        vtAsyncCh.Writer.TryWrite(new IntOrCompletion(0, completion));
        await completion.Task;

    }
        
    [Benchmark]
    public async Task UnfoldAsyncYieldInPush()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions
            .RunContinuationsAsynchronously);
        for (int i = 0; i < 100; i++)
        {
            asyncYieldCh.Writer.TryWrite(new IntOrCompletion(i, null));
            await Task.Yield();
        }

        asyncYieldCh.Writer.TryWrite(new IntOrCompletion(0, completion));
        await completion.Task;

    }
        
        
    [Benchmark]
    public async Task UnfoldValueTaskAsyncYieldInPush()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions
            .RunContinuationsAsynchronously);
        for (int i = 0; i < 100; i++)
        {
            vtAsyncYieldCh.Writer.TryWrite(new IntOrCompletion(i, null));
            await Task.Yield();
        }

        vtAsyncYieldCh.Writer.TryWrite(new IntOrCompletion(0, completion));
        await completion.Task;

    }
}