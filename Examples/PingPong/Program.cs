using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;

namespace PingPong
{
    internal class Program
    {
        private static int redCount;
        private static long bestThroughput;

        private static readonly object msg = new object();
        private static readonly object run = new object();

        public static uint CpuSpeed()
        {
#if !mono
            var mo = new ManagementObject("Win32_Processor.DeviceID='CPU0'");
            var sp = (uint) (mo["CurrentClockSpeed"]);
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
            int workerThreads;
            int completionPortThreads;
            ThreadPool.GetAvailableThreads(out workerThreads, out completionPortThreads);

            Console.WriteLine("Worker threads: {0}", workerThreads);
            Console.WriteLine("OSVersion: {0}", Environment.OSVersion);
            Console.WriteLine("ProcessorCount: {0}", Environment.ProcessorCount);
            Console.WriteLine("ClockSpeed: {0} MHZ", CpuSpeed());

            Console.WriteLine("Actor Count: {0}", Environment.ProcessorCount*2);
            Console.WriteLine("");
            Console.WriteLine("Throughput Setting, Messages/sec");

            foreach(var t in GetThroughputSettings())
            {
                await Benchmark(t);
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

        private static async Task<bool> Benchmark(int factor)
        {
            const int repeatFactor = 500;
            const long repeat = 30000L*repeatFactor;
            const long totalMessagesReceived = repeat*2;
                //times 2 since the client and the destination both send messages

            var numberOfClients = Environment.ProcessorCount;
            long repeatsPerClient = repeat/numberOfClients;
            ActorSystem system = ActorSystem.Create("PingPong");


            var clients = new List<ActorRef>();
            var tasks = new List<Task>();

            for (int i = 0; i < numberOfClients; i++)
            {
                var destination = (ActorRefWithCell)system.ActorOf<Destination>();
                destination.Cell.Dispatcher.Throughput = factor;

                var ts = new TaskCompletionSource<bool>();
                tasks.Add(ts.Task);
                var client =
                    (ActorRefWithCell)system.ActorOf(Props.Create(() => new Client(destination, repeatsPerClient, ts)));
                client.Cell.Dispatcher.Throughput = factor;
                clients.Add(client);
            }

            clients.ForEach(c => c.Tell(run));

            Stopwatch sw = Stopwatch.StartNew();

            await Task.WhenAll(tasks.ToArray());
            sw.Stop();

            system.Shutdown();
            long throughput = totalMessagesReceived/sw.ElapsedMilliseconds*1000;
            if (throughput > bestThroughput)
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

            Console.WriteLine("{0}, {1} messages/s", factor, throughput);

            if (redCount > 3)
                return false;

            return true;
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
                if (message == msg)
                {
                    received++;
                    if (sent < repeat)
                    {
                        actor.Tell(msg);
                        sent++;
                    }
                    else if (received >= repeat)
                    {
                        //       Console.WriteLine("done {0}", Self.Path);
                        latch.SetResult(true);
                    }
                }
                if (message == run)
                {
                    for (int i = 0; i < Math.Min(1000, repeat); i++)
                    {
                        actor.Tell(msg);
                        sent++;
                    }
                }
            }
        }

        public class Destination : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                if (message == msg)
                    Sender.Tell(msg);
            }
        }
    }
}