
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
                var actors = new List<LocalActorRef>();
                for (int i = 0; i < actorCount; i++)
                {
                    var actor = system.ActorOf<MessageProcessCountActor>();
                    actors.Add(actor);
                }
                
                var timeout = TimeSpan.FromSeconds(5);

                var tasks = new List<Task>();
                var sentMessages = 0L;
                Stopwatch sw = Stopwatch.StartNew();
                var message = "hello";
                
                while (sw.Elapsed < timeout)
                {
                    for (int i = 0; i < actorCount; i++)
                    {
                        Interlocked.Increment(ref sentMessages);
                        actors[i].Tell(message);
                        Interlocked.Increment(ref sentMessages);
                        actors[i].Tell(message);
                    }
                }
               
                var messageCount = actors.Sum(a =>
                {
                    return (a.Cell.Actor as MessageProcessCountActor).count;
                });
                sw.Stop();

                var throughput = messageCount / sw.ElapsedMilliseconds * 1000;

                

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
                Console.WriteLine("Actors: {0}", actorCount);
                Console.WriteLine("Messages sent: {0}/s", sentMessages / sw.ElapsedMilliseconds * 1000);
                Console.WriteLine("Messages processed {0}/s", throughput);

                if (redCount > 15)
                    return false;

                return true;
            }
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
