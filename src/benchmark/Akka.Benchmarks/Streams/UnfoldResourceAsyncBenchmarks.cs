// -----------------------------------------------------------------------
//  <copyright file="UnfoldResourceAsyncBenchmarks.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Fusing;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Streams;

[Config(typeof(MicroBenchmarkConfig))]
public class UnfoldResourceAsyncBenchmarks
{
    
    public readonly struct IntOrCompletion
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
    private Task<Done> unfoldAsyncSyncStub;
    private Channel<IntOrCompletion> asyncYieldCh;
    private Channel<IntOrCompletion> straightCh;
    private Task straightTask;
    private CancellationTokenSource straightChTokenSource;
    private Channel<IntOrCompletion> straightYieldCh;
    private Task straightYieldTask;

    [GlobalSetup]
    public void Setup()
    {
        system = ActorSystem.Create("system");
        materializer = system.Materializer();
        asyncNoYieldCh = Channel.CreateUnbounded<IntOrCompletion>();
            
        asyncYieldCh = Channel.CreateUnbounded<IntOrCompletion>();
            
            
        unfoldAsyncSyncStub = Source.UnfoldResourceAsync<int,ChannelReader<IntOrCompletion>>(()=> Task.FromResult(asyncYieldCh.Reader), async r =>
            {
                var i = await r.ReadAsync();
                if (i.Completion != null)
                {
                    i.Completion.TrySetResult();
                    return -1;
                }
                else
                {
                    return i.IntValue;
                }
            }, (r)=> Task.FromResult(Done.Instance))
            .RunWith(Sink.Ignore<int>(), materializer);
            
        selectAsyncStub = Source.UnfoldResourceAsync<int,ChannelReader<IntOrCompletion>>(()=>Task.FromResult(asyncNoYieldCh.Reader),async r =>
        { 
            await Task.Yield();
            var a = await r.ReadAsync(); 
            if (a.Completion != null)
            {
                a.Completion.TrySetResult();
                return -1;
            }
            else
            {
                //await Task.Yield();
                //        await Task.Delay(0);
                return  a.IntValue;
            }
        }, (r)=> Task.FromResult(Done.Instance) ).RunWith(Sink.Ignore<int>(), materializer);
        
        
        straightChTokenSource = new CancellationTokenSource();
        straightCh = Channel.CreateUnbounded<IntOrCompletion>();


        straightTask = Task.Run(async () =>
        {
            static async IAsyncEnumerable<int> GetEnumerator(
                ChannelReader<IntOrCompletion> reader, CancellationToken token)
            {
                while (token.IsCancellationRequested == false)
                {
                    await Task.Yield();
                    var a = await reader.ReadAsync();
                    if (a.Completion != null)
                    {
                        a.Completion.TrySetResult();
                        yield return -1;
                    }
                    else
                    {
                        //await Task.Yield();
                        //await Task.Delay(0);
                        yield return a.IntValue;
                    }
                }
            }
            var r = straightCh.Reader;
            await foreach (var v in GetEnumerator(r,straightChTokenSource.Token))
            {
                
            }
        });
        
        straightYieldCh = Channel.CreateUnbounded<IntOrCompletion>();


        straightYieldTask = Task.Run(async () =>
        {
            static async IAsyncEnumerable<int> GetEnumerator(
                ChannelReader<IntOrCompletion> reader, CancellationToken token)
            {
                while (token.IsCancellationRequested == false)
                {
                    var a = await reader.ReadAsync();
                    if (a.Completion != null)
                    {
                        a.Completion.TrySetResult();
                        yield return -1;
                    }
                    else
                    {
                        //await Task.Yield();
                        //await Task.Delay(0);
                        yield return a.IntValue;
                    }
                }
            }
            var r = straightYieldCh.Reader;
            await foreach (var v in GetEnumerator(r,straightChTokenSource.Token))
            {
                
            }
        });
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        materializer.Dispose();
        system.Dispose();
        straightChTokenSource.Cancel();
    }
        
    [Benchmark]
    public async Task UnfoldResourceAsyncNoYield()
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
    public async Task UnfoldResourceAsyncWithYield()
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
    public async Task StraightChannelReadNoYield()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions
            .RunContinuationsAsynchronously);
        for (int i = 0; i < 100; i++)
        {
            straightCh.Writer.TryWrite(new IntOrCompletion(i, null));
        }

        straightCh.Writer.TryWrite(new IntOrCompletion(0, completion));
        await completion.Task;

    }
    
    [Benchmark]
    public async Task StraightChannelReadWithYield()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions
            .RunContinuationsAsynchronously);
        for (int i = 0; i < 100; i++)
        {
            straightYieldCh.Writer.TryWrite(new IntOrCompletion(i, null));
            await Task.Yield();
        }

        straightYieldCh.Writer.TryWrite(new IntOrCompletion(0, completion));
        await completion.Task;

    }
}