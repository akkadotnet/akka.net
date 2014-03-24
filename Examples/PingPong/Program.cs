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

            Console.WriteLine("Actor count, Messages/sec");

            for (int i = 1; i < 20; i++)
            {
                if (!await Benchmark(i))
                    break;
            }
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("Done..");
        }

        private static async Task<bool> Benchmark(int numberOfClients)
        {
            const int repeatFactor = 500;
            const long repeat = 30000L*repeatFactor;
            const long totalMessagesReceived = repeat*2;
                //times 2 since the client and the destination both send messages

            long repeatsPerClient = repeat/numberOfClients;
            ActorSystem system = ActorSystem.Create("PingPong");


            var clients = new List<ActorRef>();
            var tasks = new List<Task>();
            for (int i = 0; i < numberOfClients; i++)
            {
                InternalActorRef destination = system.ActorOf<Destination>();
                var ts = new TaskCompletionSource<bool>();
                tasks.Add(ts.Task);
                InternalActorRef client =
                    system.ActorOf(Props.Create(() => new Client(destination, repeatsPerClient, ts)));
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

            Console.WriteLine("{0}, {1} messages/s", numberOfClients*2, throughput);

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