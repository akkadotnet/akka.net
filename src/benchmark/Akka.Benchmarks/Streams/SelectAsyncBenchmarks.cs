// //-----------------------------------------------------------------------
// // <copyright file="SelectAsyncBenchmarks.cs" company="Akka.NET Project">
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
public class SelectAsyncBenchmarks
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
    private Channel<IntOrCompletion> asyncCh;
    private Task<Done> selectValueTaskAsyncStub;
    private Channel<IntOrCompletion> vtAsyncCh;
    private Task<Done> selectAsyncSyncStub;
    private Task<Done> selectAsyncValueTaskSyncStub;
    private Channel<IntOrCompletion> asyncChSync;
    private Channel<IntOrCompletion> vtAsyncChSync;

    [GlobalSetup]
    public void Setup()
    {
        system = ActorSystem.Create("system");
        materializer = system.Materializer();
        asyncCh = Channel.CreateUnbounded<IntOrCompletion>();
            
        asyncChSync = Channel.CreateUnbounded<IntOrCompletion>();
            
        vtAsyncChSync = Channel.CreateUnbounded<IntOrCompletion>();
            
        selectAsyncSyncStub = Source.ChannelReader(asyncChSync.Reader)
            .SelectAsync(4, a =>
            {
                if (a.Completion != null)
                {
                    a.Completion.TrySetResult();
                }
                else
                {
                }

                return Task.FromResult(NotUsed.Instance);
            }).RunWith(Sink.Ignore<NotUsed>(), materializer);
            
        selectAsyncValueTaskSyncStub = Source.ChannelReader(vtAsyncChSync.Reader)
            .SelectValueTaskAsync(4, a =>
            {
                if (a.Completion != null)
                {
                    a.Completion.TrySetResult();
                }
                else
                {
                }

                return ValueTask.FromResult(NotUsed.Instance);
            }).RunWith(Sink.Ignore<NotUsed>(), materializer);
        selectAsyncStub = Source.ChannelReader(asyncCh.Reader)
            .SelectAsync(4, async a =>
            {
                if (a.Completion != null)
                {
                    a.Completion.TrySetResult();
                }
                else
                {
                    //await Task.Yield();
                    await Task.Delay(0);
                }

                return NotUsed.Instance;
            }).RunWith(Sink.Ignore<NotUsed>(), materializer);
        vtAsyncCh = Channel.CreateUnbounded<IntOrCompletion>();
        int vta = 0;
        selectValueTaskAsyncStub = Source.ChannelReader(vtAsyncCh.Reader)
            .SelectValueTaskAsync(4, async a =>
            {
                if (a.Completion != null)
                {
                    a.Completion.TrySetResult();
                    //return NotUsed.Instance;
                }
                else
                {
                    //await Task.Yield();
                    await Task.Delay(0);
                    //return NotUsed.Instance;
                    //Console.WriteLine(++vta);
                    //return vta;
                }

                return NotUsed.Instance;
            }).RunWith(Sink.Ignore<NotUsed>(), materializer);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        materializer.Dispose();
        system.Dispose();
    }
        
    [Benchmark]
    public async Task RunSelectAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions
            .RunContinuationsAsynchronously);
        for (int i = 0; i < 100; i++)
        {
            asyncCh.Writer.TryWrite(new IntOrCompletion(i, null));
        }

        asyncCh.Writer.TryWrite(new IntOrCompletion(0, completion));
        await completion.Task;

    }
        
        
    [Benchmark]
    public async Task RunSelectValueTaskAsync()
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
    public async Task RunSelectAsyncSync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions
            .RunContinuationsAsynchronously);
        for (int i = 0; i < 100; i++)
        {
            asyncChSync.Writer.TryWrite(new IntOrCompletion(i, null));
        }

        asyncChSync.Writer.TryWrite(new IntOrCompletion(0, completion));
        await completion.Task;

    }
        
        
    [Benchmark]
    public async Task RunSelectValueTaskAsyncSync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions
            .RunContinuationsAsynchronously);
        for (int i = 0; i < 100; i++)
        {
            vtAsyncChSync.Writer.TryWrite(new IntOrCompletion(i, null));
        }

        vtAsyncChSync.Writer.TryWrite(new IntOrCompletion(0, completion));
        await completion.Task;

    }
}