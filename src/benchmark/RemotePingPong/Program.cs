//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using Akka.Actor;
using Akka.Configuration;
using Akka.Util.Internal;

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
        public static uint CpuSpeed()
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

        public static Config CreateActorSystemConfig(string actorSystemName, string ipOrHostname, int port)
        {
            var baseConfig = ConfigurationFactory.ParseString(@"
                akka {
              actor.provider = remote
              loglevel = ERROR
              suppress-json-serializer-warning = on
              log-dead-letters = off

              remote {
                log-remote-lifecycle-events = off

                dot-netty.tcp {
                    port = 0
                    hostname = ""localhost""
                }
              }
            }");

            var bindingConfig =
                ConfigurationFactory.ParseString(@"akka.remote.dot-netty.tcp.hostname = """ + ipOrHostname + @"""")
                    .WithFallback(ConfigurationFactory.ParseString(@"akka.remote.dot-netty.tcp.port = " + port));

            return bindingConfig.WithFallback(baseConfig);
        }

        private static void Main(params string[] args)
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            uint timesToRun;
            if (args.Length == 0 || !uint.TryParse(args[0], out timesToRun))
            {
                timesToRun = 1u;
            }

            Start(timesToRun);
            Console.ReadKey();
        }

        private static async void Start(uint timesToRun)
        {
            const long repeat = 100000L;

            var processorCount = Environment.ProcessorCount;
            if (processorCount == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to read processor count..");
                return;
            }

#if THREADS
            int workerThreads;
            int completionPortThreads;
            ThreadPool.GetAvailableThreads(out workerThreads, out completionPortThreads);

            Console.WriteLine("Worker threads:                    {0}", workerThreads);
            Console.WriteLine("OSVersion:                         {0}", Environment.OSVersion);
#endif
            Console.WriteLine("ProcessorCount:                    {0}", processorCount);
            Console.WriteLine("ClockSpeed:                        {0} MHZ", CpuSpeed());
            Console.WriteLine("Actor Count:                       {0}", processorCount * 2);
            Console.WriteLine("Messages sent/received per client: {0}  ({0:0e0})", repeat*2);
            Console.WriteLine("Is Server GC:                      {0}", GCSettings.IsServerGC);
            Console.WriteLine();

            //Print tables
            Console.WriteLine("Num clients, Total [msg], Msgs/sec, Total [ms]");

            for (var i = 0; i < timesToRun; i++)
            {
                var redCount = 0;
                var bestThroughput = 0L;
                foreach (var throughput in GetClientSettings())
                {
                    var result1 = await Benchmark(throughput, repeat, bestThroughput, redCount);
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
            var totalMessagesReceived = GetTotalMessagesReceived(numberOfClients, numberOfRepeats);
            var system1 = ActorSystem.Create("SystemA", CreateActorSystemConfig("SystemA", "127.0.0.1", 0));

            var system2 = ActorSystem.Create("SystemB", CreateActorSystemConfig("SystemB", "127.0.0.1", 0));

            List<Task<long>> tasks = new List<Task<long>>();
            List<IActorRef> receivers = new List<IActorRef>();

            var canStart = system1.ActorOf(Props.Create(() => new AllStartedActor()), "canStart");

            var system1Address = ((ExtendedActorSystem)system1).Provider.DefaultAddress;
            var system2Address = ((ExtendedActorSystem)system2).Provider.DefaultAddress;

            var echoProps = Props.Create(() => new EchoActor()).WithDeploy(new Deploy(new RemoteScope(system2Address)));

            for (var i = 0; i < numberOfClients; i++)
            {
                var echo = system1.ActorOf(echoProps, "echo" + i);
                var ts = new TaskCompletionSource<long>();
                tasks.Add(ts.Task);
                var receiver =
                    system1.ActorOf(
                        Props.Create(() => new BenchmarkActor(numberOfRepeats, ts, echo)),
                        "benchmark" + i);

                receivers.Add(receiver);

                canStart.Tell(echo);
                canStart.Tell(receiver);
            }

            var rsp = await canStart.Ask(new AllStartedActor.AllStarted(), TimeSpan.FromSeconds(10));
            var testReady = (bool)rsp;
            if (!testReady)
            {
                throw new Exception("Received report that 1 or more remote actor is unable to begin the test. Aborting run.");
            }

            var sw = Stopwatch.StartNew();
            receivers.ForEach(c =>
            {
                for (var i = 0; i < 50; i++) // prime the pump so EndpointWriters can take advantage of their batching model
                    c.Tell("hit");
            });
            var waiting = Task.WhenAll(tasks);
            await Task.WhenAll(waiting);
            sw.Stop();

            // force clean termination
            var termination = Task.WhenAll(new[] { system1.Terminate(), system2.Terminate() }).Wait(TimeSpan.FromSeconds(10));

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
            Console.WriteLine("{0,10},{1,8},{2,10},{3,11}", numberOfClients, totalMessagesReceived, throughput, sw.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture));
            return (redCount <= 3, bestThroughput, redCount);
        }

        private class AllStartedActor : UntypedActor
        {
            public class AllStarted { }

            private readonly HashSet<IActorRef> _actors = new HashSet<IActorRef>();
            private int _correlationId = 0;

            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case IActorRef a:
                        _actors.Add(a);
                        break;
                    case AllStarted a:
                        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        var s = Sender;
                        var count = _actors.Count;
                        var c = _correlationId++;
                        var t = Task.WhenAll(_actors.Select(
                            x => x.Ask<ActorIdentity>(new Identify(c), cts.Token)));
                        t.ContinueWith(tr =>
                        {
                            return tr.Result.Length == count && tr.Result.All(x => x.MessageId.Equals(c));
                        }, TaskContinuationOptions.OnlyOnRanToCompletion).PipeTo(s);
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
                if (_currentMessages < _maxExpectedMessages)
                {
                    _currentMessages++;
                    _echo.Tell(message);
                }
                else
                {
                    _completion.TrySetResult(_maxExpectedMessages);
                }
            }
        }
    }
}
