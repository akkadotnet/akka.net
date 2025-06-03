//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

namespace RemotePingPong
{
    public static class Messages
    {
        public class Msg { public override string ToString() { return "msg"; } }
        public class Run { public override string ToString() { return "run"; } }
        public class Started { public override string ToString() { return "started"; } }
    }

    internal class Program
    {
        public static Config CreateActorSystemConfig(string actorSystemName, string ipOrHostname, int port, bool enableDotNettyConfigDump = true)
        {
            var configString = $@"
            akka {{
              actor.provider = remote
              loglevel = ERROR
              suppress-json-serializer-warning = on
              log-dead-letters = off

              remote {{
                log-remote-lifecycle-events = off

                dot-netty.tcp {{
                    port = {port}
                    hostname = ""{ipOrHostname}""
                }}
              }}
            }}";

            return ConfigurationFactory.ParseString(configString);
        }

        private static async Task Main(params string[] args)
        {
            var disableRecycler = args.Contains("--disable-recycler");
            var timesToRun = GetTimesToRun(args);

            // Set recycler environment variable if requested
            if (disableRecycler)
            {
                Environment.SetEnvironmentVariable("io.netty.recycler.maxCapacityPerThread", "0");
                Console.WriteLine("ThreadLocalPool recycler DISABLED (io.netty.recycler.maxCapacityPerThread=0)");
            }
            else
            {
                Console.WriteLine("ThreadLocalPool recycler ENABLED (default)");
            }

            Console.WriteLine($"Running {timesToRun} times");
            Console.WriteLine();

            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to elevate process priority: {ex.Message}");
            }

            await Start(timesToRun);
        }

        private static uint GetTimesToRun(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if ((args[i] == "--times" || args[i] == "-t") && uint.TryParse(args[i + 1], out var times))
                {
                    return times;
                }
            }
            
            // Default or legacy: first argument as times to run
            if (args.Length > 0 && uint.TryParse(args[0], out var legacyTimes))
            {
                return legacyTimes;
            }

            return 1;
        }

        const long repeat = 100000L;

        private static async Task Start(uint timesToRun)
        {
            PrintSysInfo();
            
            for (var i = 0; i < timesToRun; i++)
            {
                var redCount = 0;
                var bestThroughput = 0L;
                
                foreach (var clientCount in GetClientSettings())
                {
                    var (success, throughput, newRedCount) = await Benchmark(clientCount, repeat, bestThroughput, redCount);
                    bestThroughput = throughput;
                    redCount = newRedCount;
                }
            }

            Console.WriteLine("Done.");
        }

        private static void PrintSysInfo()
        {
            Console.WriteLine("OSVersion:                         {0}", Environment.OSVersion);
            Console.WriteLine("ProcessorCount:                    {0}", Environment.ProcessorCount);
            Console.WriteLine("Actor Count:                       {0}", Environment.ProcessorCount * 2);
            Console.WriteLine("Messages sent/received per client: {0}  ({0:0e0})", repeat * 2);
            Console.WriteLine("Is Server GC:                      {0}", System.Runtime.GCSettings.IsServerGC);
            Console.WriteLine("Thread count:                      {0}", Process.GetCurrentProcess().Threads.Count);
            Console.WriteLine();
            Console.WriteLine("Num clients, Total [msg], Msgs/sec, Total [ms], Start Threads, End Threads");
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
            var totalMessagesReceived = GetTotalMessagesReceived(numberOfClients, numberOfRepeats);
            
            // Create actor systems with DotNetty config dump enabled
            var system1 = ActorSystem.Create("SystemA", CreateActorSystemConfig("SystemA", "127.0.0.1", 0));
            var system2 = ActorSystem.Create("SystemB", CreateActorSystemConfig("SystemB", "127.0.0.1", 0));

            var tasks = new List<Task<long>>();
            var receivers = new List<IActorRef>();

            var canStart = system1.ActorOf(Props.Create(() => new AllStartedActor()), "canStart");

            var system1Address = ((ExtendedActorSystem)system1).Provider.DefaultAddress;
            var system2Address = ((ExtendedActorSystem)system2).Provider.DefaultAddress;

            var echoProps = Props.Create(() => new EchoActor()).WithDeploy(new Deploy(new RemoteScope(system2Address)));

            for (var i = 0; i < numberOfClients; i++)
            {
                var echo = system1.ActorOf(echoProps, "echo" + i);
                var ts = new TaskCompletionSource<long>();
                tasks.Add(ts.Task);
                var receiver = system1.ActorOf(Props.Create(() => new BenchmarkActor(numberOfRepeats, ts, echo)), "benchmark" + i);

                receivers.Add(receiver);
                canStart.Tell(echo);
                canStart.Tell(receiver);
            }

            var rsp = await canStart.Ask(new AllStartedActor.AllStarted(), TimeSpan.FromSeconds(10));
            var testReady = (bool)rsp;
            if (!testReady)
            {
                throw new Exception("Remote actors not ready. Aborting.");
            }

            var startThreads = Process.GetCurrentProcess().Threads.Count;
            var sw = Stopwatch.StartNew();

            foreach (var receiver in receivers)
            {
                receiver.Tell(new Messages.Run());
            }

            await Task.WhenAll(tasks);
            sw.Stop();

            var endThreads = Process.GetCurrentProcess().Threads.Count;
            var totalMessages = tasks.Sum(t => t.Result);
            var x = (int)Math.Round((double)totalMessages / sw.ElapsedMilliseconds * 1000.0d);
            var throughput = (long)x;
            var color = ConsoleColor.Green;
            if (throughput < bestThroughput)
            {
                color = ConsoleColor.Red;
                redCount++;
            }
            else
            {
                bestThroughput = throughput;
            }

            Console.ForegroundColor = color;
            Console.WriteLine("{0,10}, {1,10}, {2,10}, {3,10}, {4,10}, {5,10}",
                numberOfClients,
                totalMessages,
                throughput,
                sw.ElapsedMilliseconds,
                startThreads,
                endThreads);
            Console.ResetColor();

            await system1.Terminate();
            await system2.Terminate();

            return (true, throughput, redCount);
        }

        private class AllStartedActor : UntypedActor
        {
            public class AllStarted { }

            private readonly HashSet<IActorRef> _actors = new();
            private int _correlationId = 0;

            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case IActorRef actorRef:
                        _actors.Add(actorRef);
                        break;
                    case AllStarted _:
                        Sender.Tell(_actors.Count >= _correlationId);
                        _correlationId = _actors.Count;
                        break;
                }
            }
        }

        private class EchoActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                Sender.Tell(message);
            }
        }

        private class BenchmarkActor : UntypedActor
        {
            private readonly long _maxExpectedMessages;
            private readonly IActorRef _echo;
            private long _currentMessages = 0;
            private readonly TaskCompletionSource<long> _completion;

            public BenchmarkActor(long maxExpectedMessages, TaskCompletionSource<long> completion, IActorRef echo)
            {
                _maxExpectedMessages = maxExpectedMessages;
                _completion = completion;
                _echo = echo;
            }

            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case Messages.Run _:
                        for (var i = 0L; i < _maxExpectedMessages; i++)
                        {
                            _echo.Tell(new Messages.Msg());
                        }
                        break;
                    case Messages.Msg _:
                        _currentMessages++;
                        if (_currentMessages >= _maxExpectedMessages)
                        {
                            _completion.SetResult(_currentMessages);
                        }
                        break;
                }
            }
        }
    }
}
