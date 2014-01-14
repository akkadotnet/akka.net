
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

            ActorSystem system = new ActorSystem();

            List<LocalActorRef> actors = new List<LocalActorRef>();
            for (int i = 0; i < 1; i++)
            {
                var a1 = system.ActorOf<Client>();
                var a2 = system.ActorOf<Client>();
                a1.Tell(Run.Default, a2);
                a2.Tell(Run.Default, a1);

                actors.Add(a1);
                actors.Add(a2);
            }

            Stopwatch sw = Stopwatch.StartNew();
            var startCount = actors.Sum(a => (a.Cell.Actor as Client).received);
            Thread.Sleep(15000);
            var endCount = actors.Sum(a => (a.Cell.Actor as Client).received);
            sw.Stop();
            var diff = endCount - startCount;
            Console.WriteLine(diff / sw.ElapsedMilliseconds * 1000);
            Console.WriteLine(diff );
            Console.ReadLine();
        }

        public class Msg
        {
            public static readonly Msg Default = new Msg();
        }

        public class Run
        {
            public static readonly Run Default = new Run();       
        }

        public class Client : UntypedActor
        {
            public long received;
            public long sent;
            public long repeat = 100000000;
            public long initialMessages = 2000;
            protected override void OnReceive(object message)
            {
                if (message is Msg)
                {
                    received++;
                    if (sent < repeat)
                    {
                        Sender.Tell(Msg.Default);
                        sent++;
                    }
                }
                if (message is Run)
                {
                    for (int i = 0; i < initialMessages; i++)
                    {
                        Sender.Tell(Msg.Default);
                        sent++;
                    }
                }
            }
        }
    }
}
