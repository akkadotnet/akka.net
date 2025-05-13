//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using Akka;
using Akka.Actor;
using Akka.Cluster;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Event;

namespace PubSubPingPong;

public static class Messages
{
    public class Msg { public override string ToString() { return "msg"; } }
    public class Run { public override string ToString() { return "run"; } }
    public class Started { public override string ToString() { return "started"; } }
}

internal class Program
{
    private static uint CpuSpeed()
    {
#if THREADS
            var mo = new System.Management.ManagementObject("Win32_Processor.DeviceID='CPU0'");
            var sp = (uint)(mo["CurrentClockSpeed"]);
            mo.Dispose();
            return sp;
#else
        return 0;
#endif
    }

    private static Config CreateActorSystemConfig(string actorSystemName, string ipOrHostname, int port)
    {
        var baseConfig = ConfigurationFactory.ParseString(
            """
            akka {
              actor.provider = cluster
              loglevel = ERROR
              suppress-json-serializer-warning = on
              log-dead-letters = off

              remote {
                log-remote-lifecycle-events = off

                dot-netty.tcp {
                    port = 0
                    hostname = "localhost"
                }
              }
            }
            """);

        var bindingConfig =
            ConfigurationFactory.ParseString(@"akka.remote.dot-netty.tcp.hostname = """ + ipOrHostname + @"""")
                .WithFallback(ConfigurationFactory.ParseString(@"akka.remote.dot-netty.tcp.port = " + port));

        return bindingConfig
            .WithFallback(baseConfig)
            .WithFallback(DistributedPubSub.DefaultConfig());
    }

    private static async Task Main(params string[] args)
    {
        try
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Attempted to elevate process priority, but failed due to {ex.Message} - carrying on at normal process priority.");
        }

        if (args.Length == 0 || !uint.TryParse(args[0], out var timesToRun))
        {
            timesToRun = 1u;
        }

        await Start(timesToRun);
    }

    private static bool _firstRun = true;

    private static void PrintSysInfo(){
        var processorCount = Environment.ProcessorCount;
        if (processorCount == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Failed to read processor count..");
            return;
        }

        Console.WriteLine("OSVersion:                         {0}", Environment.OSVersion);
        Console.WriteLine("ProcessorCount:                    {0}", processorCount);
        Console.WriteLine("ClockSpeed:                        {0} MHZ", CpuSpeed());
        Console.WriteLine("Actor Count:                       {0}", processorCount * 2);
        Console.WriteLine("Messages sent/received per client: {0}  ({0:0e0})", Repeat*2);
        Console.WriteLine("Is Server GC:                      {0}", GCSettings.IsServerGC);
        Console.WriteLine("Thread count:                      {0}", Process.GetCurrentProcess().Threads.Count);
        Console.WriteLine();

        //Print tables
        Console.WriteLine("Num clients, Total [msg], Msgs/sec, Total [ms], Start Threads, End Threads");
    }

    private const long Repeat = 100000L;

    private static async Task Start(uint timesToRun)
    {
        for (var i = 0; i < timesToRun; i++)
        {
            var redCount = 0;
            var bestThroughput = 0L;
            foreach (var throughput in GetClientSettings())
            {
                var result1 = await Benchmark(throughput, Repeat, bestThroughput, redCount);
                bestThroughput = result1.Item2;
                redCount = result1.Item3;
            }
        }

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("Done..");
    }

    public static IEnumerable<int> GetClientSettings()
    {
        yield return 1;
        yield return 5;
        yield return 10;
        yield return 15;
        yield return 20;
        yield return 25;
        yield return 30;
    }

    private static long GetTotalMessagesReceived(int numberOfClients, long numberOfRepeats)
    {
        return numberOfClients * numberOfRepeats * 2;
    }

    private static async Task<(bool, long, int)> Benchmark(int numberOfClients, long numberOfRepeats, long bestThroughput, int redCount)
    {
        var system1 = ActorSystem.Create("System", CreateActorSystemConfig("SystemA", "127.0.0.1", 0));

        var system1Address = ((ExtendedActorSystem)system1).Provider.DefaultAddress;

        Cluster.Get(system1).Join(system1Address);

        var totalMessagesReceived = GetTotalMessagesReceived(numberOfClients, numberOfRepeats);
        var actorReadyTasks = new List<Task>();
        var tasks = new List<Task<long>>();
        var receivers = new List<IActorRef>();

        for (var i = 0; i < numberOfClients; i++)
        {
            var topic = $"topic-{i}";
            var tcs1 = new TaskCompletionSource<NotUsed>();
            actorReadyTasks.Add(tcs1.Task);
            var echoProps = Props.Create(() => new EchoActor(topic, null, tcs1));
            var echo = system1.ActorOf(echoProps, $"echo-{i}");
            
            var tc = new TaskCompletionSource<long>();
            tasks.Add(tc.Task);
            var tcs2 = new TaskCompletionSource<NotUsed>();
            actorReadyTasks.Add(tcs2.Task);
            var receiver =
                system1.ActorOf(
                    Props.Create(() => new BenchmarkActor(numberOfRepeats, tc, topic, tcs2)),
                    $"benchmark-{i}");

            receivers.Add(receiver);
        }

        await Task.WhenAll(actorReadyTasks);

        // now that the dispatchers in both ActorSystems are started, we want to measure thread count and other system
        // metrics here - but only the very first benchmark
        if(_firstRun){
            PrintSysInfo();
            _firstRun = false;
        }

        var startThreads = Process.GetCurrentProcess().Threads.Count;

        var sw = Stopwatch.StartNew();
        receivers.ForEach(c =>
        {
            for (var i = 0; i < 50; i++) // prime the pump so EndpointWriters can take advantage of their batching model
                c.Tell("hit");
        });
        var waiting = Task.WhenAll(tasks);
        await Task.WhenAll(waiting);
        sw.Stop();
            
        var endThreads = Process.GetCurrentProcess().Threads.Count;

        // force clean termination
        await system1.Terminate();

        var elapsedMilliseconds = sw.ElapsedMilliseconds;
        long throughput = elapsedMilliseconds == 0 ? -1 : (long)Math.Ceiling((double)totalMessagesReceived / elapsedMilliseconds * 1000);
        var foregroundColor = Console.ForegroundColor;
        if (throughput >= bestThroughput)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            bestThroughput = throughput;
            redCount = 0;
        }
        else
        {
            redCount++;
            Console.ForegroundColor = ConsoleColor.Red;
        }

        Console.ForegroundColor = foregroundColor;
        Console.WriteLine("{0,10},{1,8},{2,10},{3,11}, {4,13}, {5,15}", numberOfClients, totalMessagesReceived, throughput, sw.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture), startThreads, endThreads);
        return (redCount <= 3, bestThroughput, redCount);
    }

    private class EchoActor : UntypedActor
    {
        private readonly string _topic;
        private readonly string? _group;
        private readonly TaskCompletionSource<NotUsed> _tcs;
        
        public EchoActor(string topic, string? group, TaskCompletionSource<NotUsed> tcs)
        {
            _topic = topic;
            _group = group;
            _tcs = tcs;
        }

        protected override void PreStart()
        {
            base.PreStart();
            var mediator = DistributedPubSub.Get(Context.System).Mediator;
            mediator.Tell(new Subscribe(_topic, Self, _group));
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case SubscribeAck ack:
                    _tcs.TrySetResult(NotUsed.Instance);
                    Become(Ready);
                    break;
                default:
                    Unhandled(message);
                    break;
            }
        }

        private void Ready(object msg)
        {
            Sender.Tell(msg);
        }
    }

    private class BenchmarkActor : UntypedActor
    {
        private readonly long _maxExpectedMessages;
        private readonly string _topic;
        private IActorRef _mediator;
        private long _currentMessages;
        private readonly TaskCompletionSource<long> _completion;
        private readonly TaskCompletionSource<NotUsed> _readyTcs;

        public BenchmarkActor(long maxExpectedMessages, TaskCompletionSource<long> completion, string topic, TaskCompletionSource<NotUsed> readyTcs)
        {
            _maxExpectedMessages = maxExpectedMessages;
            _completion = completion;
            _topic = topic;
            _readyTcs = readyTcs;
        }

        protected override void PreStart()
        {
            base.PreStart();
            _mediator = DistributedPubSub.Get(Context.System).Mediator;
            _readyTcs.TrySetResult(NotUsed.Instance);
        }

        protected override void OnReceive(object message)
        {
            if (_currentMessages < _maxExpectedMessages)
            {
                _currentMessages++;
                _mediator.Tell(new Publish(_topic, message));
            }
            else
            {
                _completion.TrySetResult(_maxExpectedMessages);
            }
        }
    }
}