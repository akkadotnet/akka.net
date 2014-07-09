using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.Remoting.Messaging;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;
using Akka.Event;

namespace PingPong
{
    internal class Program
    {
        private static int redCount;
        private static long bestThroughput;

        private static readonly object msg = new Message("msg");
        private static readonly object run = new Message("run");
        private static readonly object started = new Message("started");

        public static uint CpuSpeed()
        {
#if !mono
            var mo = new ManagementObject("Win32_Processor.DeviceID='CPU0'");
            var sp = (uint)(mo["CurrentClockSpeed"]);
            mo.Dispose();
            return sp;
#else
            return 0;
#endif
        }

        private static void Main()
        {
            Start();
            Console.ReadKey();
        }

        private static async void Start()
        {
            const int repeatFactor = 500;
            const long repeat = 30000L * repeatFactor;

            var processorCount = Environment.ProcessorCount;
            if(processorCount == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to read processor count..");
                return;
            }

            int workerThreads;
            int completionPortThreads;
            ThreadPool.GetAvailableThreads(out workerThreads, out completionPortThreads);

            Console.WriteLine("Worker threads:         {0}", workerThreads);
            Console.WriteLine("OSVersion:              {0}", Environment.OSVersion);
            Console.WriteLine("ProcessorCount:         {0}", processorCount);
            Console.WriteLine("ClockSpeed:             {0} MHZ", CpuSpeed());
            Console.WriteLine("Actor Count:            {0}", processorCount * 2);
            Console.WriteLine("Messages sent/received: {0}  ({0:0e0})", GetTotalMessagesReceived(repeat));
            Console.WriteLine();

            //Warm up
            await Benchmark(1, 1, 1, false);
            Console.WriteLine();

            Console.WriteLine("Throughput, Messages/sec, Start system [ms], Total time [ms]");

            foreach(var throughput in GetThroughputSettings())
            {
                await Benchmark(throughput, processorCount, repeat);
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
            for(int i = 20; i < 100; i += 10)
            {
                yield return i;
            }
            for(int i = 100; i < 1000; i += 100)
            {
                yield return i;
            }
        }

        private static async Task<bool> Benchmark(int factor, int numberOfClients, long numberOfRepeats, bool printStats = true)
        {
            var totalMessagesReceived = GetTotalMessagesReceived(numberOfRepeats);
            //times 2 since the client and the destination both send messages
            long repeatsPerClient = numberOfRepeats / numberOfClients;
            var totalWatch = Stopwatch.StartNew();

            var system = ActorSystem.Create("PingPong");

            var countdown = new CountdownEvent(numberOfClients * 2);
            var waitForStartsActor = system.ActorOf(Props.Create(() => new WaitForStarts(countdown)), "wait-for-starts");
            var clients = new List<ActorRef>();
            var tasks = new List<Task>();

            for(int i = 0; i < numberOfClients; i++)
            {
                var destination = (LocalActorRef)system.ActorOf<Destination>("destination-" + i);
                destination.Cell.Dispatcher.Throughput = factor;

                var ts = new TaskCompletionSource<bool>();
                tasks.Add(ts.Task);
                var client = (LocalActorRef)system.ActorOf(Props.Create(() => new Client(destination, repeatsPerClient, ts)), "client-" + i);
                client.Cell.Dispatcher.Throughput = factor;
                clients.Add(client);

                client.Tell(started, waitForStartsActor);
                destination.Tell(started, waitForStartsActor);
            }
            if(!countdown.Wait(TimeSpan.FromSeconds(10)))
            {
                Console.WriteLine("The system did not start in 10 seconds. Aborting.");
                return false;
            }
            var setupTime = totalWatch.Elapsed;
            var sw = Stopwatch.StartNew();
            clients.ForEach(c => c.Tell(run));

            await Task.WhenAll(tasks.ToArray());
            sw.Stop();

            system.Shutdown();
            totalWatch.Stop();

            var elapsedMilliseconds = sw.ElapsedMilliseconds;
            long throughput = elapsedMilliseconds == 0 ? -1 : totalMessagesReceived / elapsedMilliseconds * 1000;
            var foregroundColor = Console.ForegroundColor;
            if(throughput > bestThroughput)
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
            if(printStats)
                Console.WriteLine("{0,10}, {1,12}, {2,17}, {3,15}", factor, throughput, setupTime.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture), totalWatch.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture));
            Console.ForegroundColor = foregroundColor;

            if(redCount > 3)
                return false;

            return true;
        }

        private static long GetTotalMessagesReceived(long numberOfRepeats)
        {
            return numberOfRepeats * 2;
        }

        public class Client : UntypedActor
        {
            private readonly ActorRef actor;
            private readonly TaskCompletionSource<bool> latch;
            public long received;
            public long repeat;
            public long sent;

            public Client(ActorRef actor, long repeat, TaskCompletionSource<bool> latch)
            {
                this.actor = actor;
                this.repeat = repeat;
                this.latch = latch;
                //     Console.WriteLine("starting {0}", Self.Path);
            }

            protected override void OnReceive(object message)
            {
                if(message == msg)
                {
                    received++;
                    if(sent < repeat)
                    {
                        actor.Tell(msg);
                        sent++;
                    }
                    else if(received >= repeat)
                    {
                        //       Console.WriteLine("done {0}", Self.Path);
                        latch.SetResult(true);
                    }
                }
                else if(message == run)
                {
                    for(int i = 0; i < Math.Min(1000, repeat); i++)
                    {
                        actor.Tell(msg);
                        sent++;
                    }
                }
                else if(message == started)
                {
                    Sender.Tell(started);
                }
            }
        }

        public class Destination : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                if(message == msg)
                    Sender.Tell(msg);
                else if(message == started)
                {
                    Sender.Tell(started);
                }
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
                if(message == started)
                    _countdown.Signal();
            }
        }
    }

    public class Message
    {
        private readonly string _description;

        public Message(string description)
        {
            _description = description;
        }

        public override string ToString()
        {
            return _description;
        }
    }
}