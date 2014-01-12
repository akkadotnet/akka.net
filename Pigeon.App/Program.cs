
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
            int workerThreads;
            int completionPortThreads;
            ThreadPool.GetAvailableThreads(out workerThreads, out completionPortThreads);

            ThreadPool.SetMinThreads(1000, 1000);
           
            Console.WriteLine("Worker threads: {0}", workerThreads);
            Console.WriteLine("OSVersion: {0}", Environment.OSVersion);
            Console.WriteLine("ProcessorCount: {0}",Environment.ProcessorCount);
            Console.WriteLine("ClockSpeed: {0} MHZ", CPUSpeed());

            int i = 1;
            Console.WriteLine("Actor count, Messages/sec");
            while (ProfileThroughput(i++))
            {
            }
        }

        private static long bestThroughput = 0;
        private static int redCount = 0;
        private static bool ProfileThroughput(int actorCount)
        {
            GC.Collect();
            using (var system = new ActorSystem())
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                List<Task<long>> tasks = new List<Task<long>>();
                for (int i = 0; i < actorCount; i++)
                {
                    var actor = system.ActorOf<MessageProcessCountActor>();
                    var task = RunActor(actor);
                    tasks.Add(task);
                }
                Task.WaitAll(tasks.ToArray());
                var throughput = tasks.Sum(t => t.Result);
                if (throughput > bestThroughput)
                {
                    bestThroughput = throughput;
                    Console.ForegroundColor = ConsoleColor.Green;
                    redCount = 0;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    redCount++;
                }
                Console.WriteLine("{0}, {1}",actorCount, throughput);

                if (redCount > 20)
                    return false;

                return true;
            }
        }

        private static async Task<long> RunActor(LocalActorRef actor)
        {
            await Task.Yield();
      //      Console.WriteLine("start work {0}",  System.Threading.Thread.CurrentThread.GetHashCode());

            var sw = Stopwatch.StartNew();
            var message = "hello";

            for (int i = 0; i < 10000000; i++)
            {
                if (i % 10000 == 0)
                    await Task.Yield();

                actor.Tell(message);
            }
            await Task.Yield();
            sw.Stop();
            actor.Cell.Kill();
            await Task.Delay(2000);

            var messageCount = (actor.Cell.Actor as MessageProcessCountActor).count;
            var throughput = messageCount / sw.ElapsedMilliseconds * 1000;
            Console.WriteLine("done {0} - {1}", throughput,System.Threading.Thread.CurrentThread.GetHashCode());
          //  if (throughput == 0)
          //      Console.WriteLine("wwhat!");
            
            return throughput;
        }
    }

    public class MessageProducerActor : UntypedActor
    {

        protected override void OnReceive(object message)
        {
            
        }
    }

    public class MessageProcessCountActor : UntypedActor
    { 
        public long count = 0;
        protected override void OnReceive(object message)
        {
            count++;
        }
    }
}
