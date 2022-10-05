// //-----------------------------------------------------------------------
// // <copyright file="DispatcherBenchmarks.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Configuration;
using Akka.Dispatch;
using BenchmarkDotNet.Attributes;
using IRunnable = Akka.Dispatch.IRunnable;

namespace Akka.Benchmarks.Dispatch
{
    /// <summary>
    /// Used to test the performance of various dispatchers
    /// </summary>
    [Config(typeof(MicroBenchmarkConfig))]
    public class DispatcherBenchmarks
    {
        private ActorSystem _sys;
        private DefaultDispatcherPrerequisites _prereqs;

        private MessageDispatcher _dispatcher;

        [Params(1_000_000)] // higher values will cause the CallingThreadDispatcher to stack overflow
        public int TaskCount { get; set; }

        [ParamsSource(nameof(AllConfigurators))]
        public DispatcherConfig Configurator { get; set; }

        public class DispatcherConfig
        {
            public DispatcherConfig(Config config, string name)
            {
                Config = config;
                Name = name;
            }

            public Config Config { get; }

            public string Name { get; }

            public override string ToString()
            {
                return Name;
            }
        }

        private static readonly Config DefaultConfig = Akka.Remote.Configuration.RemoteConfigFactory.Default()
            .WithFallback(Akka.Configuration.ConfigurationFactory.Default());

        public IEnumerable<DispatcherConfig> AllConfigurators()
        {
            yield return new DispatcherConfig(DefaultConfig.GetConfig("akka.actor.default-dispatcher"), "DefaultThreadPool");
            yield return new DispatcherConfig(DefaultConfig.GetConfig("akka.actor.internal-dispatcher"), "ForkJoinDispatcher(Default /system)");
            yield return new DispatcherConfig(DefaultConfig.GetConfig("akka.remote.default-remote-dispatcher"), "ForkJoinDispatcher(Default /remoting)");
            yield return new DispatcherConfig(@" executor = channel-executor
                throughput=30", "ChannelDispatcher(default priority)");
            yield return new DispatcherConfig(@" executor = task-executor
                throughput=30", "TaskDispatcher");
        }

        private TaskCompletionSource<int> _tcs;

        public sealed class RunnableTarget : IRunnable
        {
            private readonly TaskCompletionSource<int> _tcs;
            private readonly int _target;
            private int _counter = 0;

            public RunnableTarget(TaskCompletionSource<int> tcs, int target)
            {
                _tcs = tcs;
                _target = target;
            }

            public void Run()
            {
                if (Interlocked.Increment(ref _counter) >= _target)
                {
                    _tcs.TrySetResult(_counter);
                }
            }
        }

        private RunnableTarget _runnable;


        [GlobalSetup]
        public void Setup()
        {
            _sys = ActorSystem.Create("Bench");
            _prereqs = new DefaultDispatcherPrerequisites(_sys.EventStream, _sys.Scheduler, _sys.Settings,
                _sys.Mailboxes);
            var configurator = new DispatcherConfigurator(Configurator.Config, _prereqs);
            _dispatcher = configurator.Dispatcher();
            _tcs = new TaskCompletionSource<int>();
            _runnable = new RunnableTarget(_tcs, TaskCount);
        }

        [GlobalCleanup]
        public void CleanUp()
        {
            _sys.Terminate().Wait();
        }

        [Benchmark]
        public async Task RunDispatcher()
        {
            for (var i = 0; i < TaskCount; i++)
                _dispatcher.Schedule(_runnable);
            await _tcs.Task;
        }
    }
}