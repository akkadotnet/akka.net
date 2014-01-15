
using Pigeon.Actor;
using Pigeon.SignalR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pigeon.App
{
    class Program
    {
        public static uint CPUSpeed()
        {
            ManagementObject Mo = new ManagementObject("Win32_Processor.DeviceID='CPU0'");
            uint sp = (uint)(Mo["CurrentClockSpeed"]);
            Mo.Dispose();
            return sp;
        }

        static void Main(string[] args)
        {
            //var sw = Stopwatch.StartNew();
            //int tmp=0;
            //for (int i = 0; i < 20000000; i++)
            //{
            //    NaiveThreadPool.Schedule(_ => tmp++);
            //}
            //Console.WriteLine(tmp);
            //Console.WriteLine(sw.Elapsed);
            //Console.ReadLine();

            int workerThreads;
            int completionPortThreads;
            ThreadPool.GetAvailableThreads(out workerThreads, out completionPortThreads);

            ThreadPool.SetMinThreads(1000, 1000);

            Console.WriteLine("Worker threads: {0}", workerThreads);
            Console.WriteLine("OSVersion: {0}", Environment.OSVersion);
            Console.WriteLine("ProcessorCount: {0}", Environment.ProcessorCount);
            Console.WriteLine("ClockSpeed: {0} MHZ", CPUSpeed());

            Console.WriteLine("Actor count, Messages/sec");

            for (int i = 1; i < 20; i++)
            {
                if (!Benchmark(i))
                    break;
            }
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("Done..");
            Console.ReadKey();
        }

        private static int redCount = 0;
        private static long bestThroughput = 0;
        private static bool Benchmark(int actorPairs)
        {
            ActorSystem system = new ActorSystem();

            List<LocalActorRef> actors = new List<LocalActorRef>();
            for (int i = 0; i < actorPairs; i++)
            {
                var a1 = system.ActorOf<Client>();
                var a2 = system.ActorOf<Client>();
                a1.Tell(Run, a2);
                a2.Tell(Run, a1);

                actors.Add(a1);
                actors.Add(a2);
            }
            Stopwatch sw = Stopwatch.StartNew();
            var startCount = actors.Sum(a => (a.Cell.Actor as Client).received);
            Thread.Sleep(10000);
            var endCount = actors.Sum(a => (a.Cell.Actor as Client).received);
            sw.Stop();
            var diff = endCount - startCount;
            system.Shutdown();
            long throughput = diff / sw.ElapsedMilliseconds * 1000;
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

            Console.WriteLine("{0}, {1} messages/s",actorPairs*2, throughput );
            WaitForEmptyThreadPool();
            if (redCount > 3)
                return false;

            return true;
        }

        private static void WaitForEmptyThreadPool()
        {
            int count = 100;
            var tasks = new Task[count];
            for (int i = 0; i < count; i++)
            {
                tasks[i] = Task.Factory.StartNew(() => { });
            }

            Task.WaitAll(tasks);
        }

        private static object Msg = new object();
        private static object Run = new object();

        public class Client : UntypedActor
        {
            public long received;
            public long sent;
            public long repeat = 100000000;
            public long initialMessages = 2000;
            protected override void OnReceive(object message)
            {
                if (message == Msg)
                {
                    received++;
                    if (sent < repeat)
                    {
                        Sender.Tell(Msg);
                        sent++;
                    }
                }
                if (message == Run)
                {
                    for (int i = 0; i < initialMessages; i++)
                    {
                        Sender.Tell(Msg);
                        sent++;
                    }
                }
            }
        }
    }
}
