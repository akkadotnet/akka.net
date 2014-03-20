﻿
using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Benchmark.PingPong
{
    class Program
    {
        public static uint CPUSpeed()
        {
#if !mono
            ManagementObject Mo = new ManagementObject("Win32_Processor.DeviceID='CPU0'");
            uint sp = (uint)(Mo["CurrentClockSpeed"]);
            Mo.Dispose();
            return sp;
#else
            return 0;
#endif
        }

        static void Main(string[] args)
        {
            int workerThreads;
            int completionPortThreads;
            ThreadPool.GetAvailableThreads(out workerThreads, out completionPortThreads);

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
        private static bool Benchmark(int numberOfClients)
        {
            var repeatFactor = 500;
            var repeat = 30000L * repeatFactor;
            var repeatsPerClient = repeat / numberOfClients;
            var system = new ActorSystem("PingPong");
            

            var clients = new List<ActorRef>();
            var tasks = new List<Task>();
            for (int i = 0; i < numberOfClients; i++)
            {
                var destination = system.ActorOf<Destination>();
                var ts = new TaskCompletionSource<bool>();
                tasks.Add(ts.Task);
                var client = system.ActorOf(Props.Create(() => new Client(destination,repeatsPerClient,ts)));                
                clients.Add(client);
            }

            clients.ForEach(c => c.Tell(Run));

            var sw = Stopwatch.StartNew();
            Task.WaitAll(tasks.ToArray());
            sw.Stop();
            var totalMessagesReceived = repeat * 2; //times 2 since the client and the destination both send messages
            system.Shutdown();
            long throughput = totalMessagesReceived / sw.ElapsedMilliseconds * 1000;
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

            Console.WriteLine("{0}, {1} messages/s", numberOfClients * 2, throughput);

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

        public class Destination : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                if (message == Msg)
                    Sender.Tell(Msg);
            }
        }

        public class Client : UntypedActor
        {
            public long received;
            public long sent;

            public long repeat;
            private ActorRef actor;
            private TaskCompletionSource<bool> latch;

            public Client(ActorRef actor,long repeat,TaskCompletionSource<bool> latch )
            {
                this.actor = actor;
                this.repeat = repeat;
                this.latch = latch;
            }
            protected override void OnReceive(object message)
            {
                if (message == Msg)
                {
                    received++;
                    if (sent < repeat)
                    {
                        actor.Tell(Msg);
                        sent++;
                    }
                    else if (received >= repeat)
                    {
                        latch.SetResult(true);
                    }
                }
                if (message == Run)
                {
                    for (int i = 0; i < Math.Min(1000,repeat); i++)
                    {
                        actor.Tell(Msg);
                        sent++;
                    }
                }
            }
        }
    }
}
