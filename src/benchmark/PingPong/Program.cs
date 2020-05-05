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

namespace PingPong
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

        private static void Main(params string[] args)
        {
            uint timesToRun;
            if (args.Length == 0 || !uint.TryParse(args[0], out timesToRun))
            {
                timesToRun = 1u;
            }

            bool testAsync = args.Contains("--async");

            Start(timesToRun, testAsync);
            Console.ReadKey();
        }

        private static async void Start(uint timesToRun, bool testAsync)
        {
            const int repeatFactor = 500;
            const long repeat = 30000L * repeatFactor;

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

            Console.WriteLine("Worker threads:         {0}", workerThreads);
            Console.WriteLine("OSVersion:              {0}", Environment.OSVersion);
#endif
            Console.WriteLine("ProcessorCount:         {0}", processorCount);
            Console.WriteLine("ClockSpeed:             {0} MHZ", CpuSpeed());
            Console.WriteLine("Actor Count:            {0}", processorCount * 2);
            Console.WriteLine("Messages sent/received: {0}  ({0:0e0})", GetTotalMessagesReceived(repeat));
            Console.WriteLine("Is Server GC:           {0}", GCSettings.IsServerGC);
            Console.WriteLine();

            //Warm up
            ActorSystem.Create("WarmupSystem").Terminate();
            Console.Write("ActorBase    first start time: ");
            await Benchmark<ClientActorBase>(1, 1, 1, PrintStats.StartTimeOnly, -1, -1);
            Console.WriteLine(" ms");
            Console.Write("ReceiveActor first start time: ");
            await Benchmark<ClientReceiveActor>(1, 1, 1, PrintStats.StartTimeOnly, -1, -1);
            Console.WriteLine(" ms");

            if (testAsync)
            {
                Console.Write("AsyncActor   first start time: ");
                await Benchmark<ClientAsyncActor>(1, 1, 1, PrintStats.StartTimeOnly, -1, -1);
                Console.WriteLine(" ms");
            }

            Console.WriteLine();

            Console.Write("            ActorBase                          ReceiveActor");
            if (testAsync)
            {
                Console.Write("                       AsyncActor");
            }
            Console.WriteLine();

            Console.Write("Throughput, Msgs/sec, Start [ms], Total [ms],  Msgs/sec, Start [ms], Total [ms]");
            if (testAsync)
            {
                Console.Write(",  Msgs/sec, Start [ms], Total [ms]");
            }
            Console.WriteLine();

            for (var i = 0; i < timesToRun; i++)
            {
                var redCountActorBase=0;
                var redCountReceiveActor=0;
                var redCountAsyncActor = 0;
                var bestThroughputActorBase=0L;
                var bestThroughputReceiveActor=0L;
                var bestThroughputAsyncActor = 0L;
                foreach(var throughput in GetThroughputSettings())
                {
                    var result1 = await Benchmark<ClientActorBase>(throughput, processorCount, repeat, PrintStats.LineStart | PrintStats.Stats, bestThroughputActorBase, redCountActorBase);
                    bestThroughputActorBase = result1.Item2;
                    redCountActorBase = result1.Item3;
                    Console.Write(",  ");
                    var result2 = await Benchmark<ClientReceiveActor>(throughput, processorCount, repeat, PrintStats.Stats, bestThroughputReceiveActor, redCountReceiveActor);
                    bestThroughputReceiveActor = result2.Item2;
                    redCountReceiveActor = result2.Item3;
                    
                    if (testAsync)
                    {
                        Console.Write(",  ");
                        var result3 = await Benchmark<ClientAsyncActor>(throughput, processorCount, repeat, PrintStats.Stats, bestThroughputAsyncActor, redCountAsyncActor);
                        bestThroughputAsyncActor = result3.Item2;
                        redCountAsyncActor = result3.Item3;
                    }

                    Console.WriteLine();
                }
            }

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("Done..");
        }

        public static IEnumerable<int> GetThroughputSettings()
        {
            yield return 1;
            yield return 5;
            yield return 10;
            yield return 15;
            for (int i = 20; i < 100; i += 10)
            {
                yield return i;
            }
            for (int i = 100; i < 1000; i += 100)
            {
                yield return i;
            }
        }

        private static async Task<(bool, long, int)> Benchmark<TActor>(int factor, int numberOfClients, long numberOfRepeats, PrintStats printStats, long bestThroughput, int redCount) where TActor : ActorBase
        {
            var totalMessagesReceived = GetTotalMessagesReceived(numberOfRepeats);
            //times 2 since the client and the destination both send messages
            long repeatsPerClient = numberOfRepeats / numberOfClients;
            var totalWatch = Stopwatch.StartNew();

            var system = ActorSystem.Create("PingPong", ConfigurationFactory.ParseString("akka.loglevel = ERROR"));

            var countdown = new CountdownEvent(numberOfClients * 2);
            var waitForStartsActor = system.ActorOf(Props.Create(() => new WaitForStarts(countdown)), "wait-for-starts");
            var clients = new List<IActorRef>();
            var tasks = new List<Task>();
            var started = new Messages.Started();
            for (int i = 0; i < numberOfClients; i++)
            {
                var destination = (RepointableActorRef)system.ActorOf<Destination>("destination-" + i);
                SpinWait.SpinUntil(() => destination.IsStarted);
                destination.Underlying.AsInstanceOf<ActorCell>().Dispatcher.Throughput = factor;

                var ts = new TaskCompletionSource<bool>();
                tasks.Add(ts.Task);
                var client = (RepointableActorRef)system.ActorOf(new Props(typeof(TActor), null, destination, repeatsPerClient, ts), "client-" + i);
                SpinWait.SpinUntil(() => client.IsStarted);
                client.Underlying.AsInstanceOf<ActorCell>().Dispatcher.Throughput = factor;
                clients.Add(client);

                client.Tell(started, waitForStartsActor);
                destination.Tell(started, waitForStartsActor);
            }
            if (!countdown.Wait(TimeSpan.FromSeconds(10)))
            {
                Console.WriteLine("The system did not start in 10 seconds. Aborting.");
                return (false, bestThroughput, redCount);
            }
            var setupTime = totalWatch.Elapsed;
            var sw = Stopwatch.StartNew();
            var run = new Messages.Run();
            clients.ForEach(c => c.Tell(run));

            await Task.WhenAll(tasks.ToArray());
            sw.Stop();

            system.Terminate();
            totalWatch.Stop();

            var elapsedMilliseconds = sw.ElapsedMilliseconds;
            long throughput = elapsedMilliseconds == 0 ? -1 : totalMessagesReceived / elapsedMilliseconds * 1000;
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
            if (printStats.HasFlag(PrintStats.StartTimeOnly))
            {
                Console.Write("{0,5}", setupTime.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture));
            }
            else
            {
                if (printStats.HasFlag(PrintStats.LineStart))
                    Console.Write("{0,10}, ", factor);
                if (printStats.HasFlag(PrintStats.Stats))
                    Console.Write("{0,8}, {1,10}, {2,10}", throughput, setupTime.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture), totalWatch.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture));
            }
            Console.ForegroundColor = foregroundColor;

            return (redCount <= 3, bestThroughput, redCount);
        }

        private static long GetTotalMessagesReceived(long numberOfRepeats)
        {
            return numberOfRepeats * 2;
        }

        public class Destination : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                if (message is Messages.Msg)
                    Sender.Tell(message);
                else if (message is Messages.Started)
                    Sender.Tell(message);
            }
        }

        public class WaitForStarts : UntypedActor
        {
            private readonly CountdownEvent _countdown;

            public WaitForStarts(CountdownEvent countdown)
            {
                _countdown = countdown;
            }

            protected override void OnReceive(object message)
            {
                if (message is Messages.Started)
                    _countdown.Signal();
            }
        }
        
        [Flags]
        public enum PrintStats
        {
            No = 0,
            LineStart = 1,
            Stats = 2,
            StartTimeOnly=32768,
        }
    }
}
