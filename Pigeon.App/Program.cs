
using Pigeon.Actor;
using Pigeon.SignalR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pigeon.App
{
    class Program
    {
        static void Main(string[] args)
        {
      //      ThreadPool.SetMinThreads(2000, 2000);
            using (var system = new ActorSystem())
            {
                var actor1 = system.ActorOf<MyActor>();
                var actor2 = system.ActorOf<MyActor>();
                var actor3 = system.ActorOf<MyActor>();
                var actor4 = system.ActorOf<MyActor>();
                actor1.Tell(new TimeRequest());
                Stopwatch sw = Stopwatch.StartNew();
                var message = new Greet { Who = "Roger" };
                for (int i = 0; i < 2000000; i++)
                {
                    actor1.Tell(message);
                    actor2.Tell(message);
                    actor3.Tell(message);
                    actor4.Tell(message);
                    //      System.Threading.Thread.Sleep(5);
                }
                
                //for (int i = 0; i < 1000; i++)
                //{
                //    actor.Tell(new Greet
                //    {
                //        Name = "Roger",
                //    }, ActorRef.NoSender);
                //    actor.Tell(new Greet
                //    {
                //        Name = "Olle",
                //    }, ActorRef.NoSender);
                //}
             //   System.Threading.Thread.Sleep(600);
                Console.WriteLine(sw.Elapsed);
                var c = (actor1.Cell.Actor as MyActor).count;
                Console.WriteLine(c);
                c = (actor2.Cell.Actor as MyActor).count;
                Console.WriteLine(c);
                c = (actor3.Cell.Actor as MyActor).count;
                Console.WriteLine(c);
                c = (actor4.Cell.Actor as MyActor).count;
                Console.WriteLine(c);
                Console.ReadLine();
            }
        }
    }

    public class Greet 
    {
        public string Who { get; set; }
    }

    public class GreetingActor : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            Pattern.Match(message)
                .With<Greet>(m => Console.WriteLine("Hello {0}", m.Who));
        }
    }

    public class LogMessage
    {
        public LogMessage(object message)
        {
            this.Timestamp = DateTime.Now;
            this.Message = message;
        }
        public DateTime Timestamp { get;private set; }
        public object Message { get; private set; }
    }

    public class TimeRequest 
    {
    }

    public class TimeResponse 
    {
        public DateTime DateTime { get; set; }
    }

    public class LogActor : UntypedActor 
    {
        protected override void OnReceive(object message)
        {
            Pattern.Match(message)
                .With<LogMessage>(m =>
                {
                    Console.WriteLine("Log {0}", m.Timestamp);
                 //   throw new NotSupportedException("Some exception");
                })
                .With<TimeRequest>(m =>
                {
                    Sender.Tell(new TimeResponse
                    {
                        DateTime = DateTime.Now
                    });
                })
                .Default(Unhandled);
        }
    }

    public class MyActor : UntypedActor
    {
        private ActorRef logger = Context.ActorOf<LogActor>();

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(
                maxNumberOfRetries: 10, 
                duration: TimeSpan.FromSeconds(30), 
                decider: x =>
                {
                    if (x is ArithmeticException)
                        return Directive.Resume;
                    if (x is NotSupportedException)
                        return Directive.Stop;

                    return Directive.Restart;
                });
        }

        public int count = 0;
        protected override void OnReceive(object message)
        {
            count++;
        //    Console.WriteLine("actor thread: {0}", System.Threading.Thread.CurrentThread.GetHashCode());
            //Pattern.Match(message)
            //    .With<Greet>(m => 
            //    {
            //        count++;
            // //       Console.WriteLine("Hello {0}", m.Who); 
            //    })
            //    .With<TimeRequest>(async m =>
            //    {
            //        //TODO: this will execute in another thread, fix
            //        Pattern.Match(await Ask(logger, m))
            //            .With<TimeResponse>(t =>
            //            {
            //                Console.WriteLine("await thread {0}", System.Threading.Thread.CurrentThread.GetHashCode());
            //                //     Console.WriteLine("its {0} o'clock", t.DateTime);
            //            })
            //            .Default(_ => Console.WriteLine("Unknown message"));

            //    })
            //    .Default(Unhandled);

            //    logger.Tell(new LogMessage(message));
        }
    }
}
