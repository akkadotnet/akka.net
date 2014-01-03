
using Pigeon.Actor;
using Pigeon.SignalR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.App
{
    class Program
    {
        static void Main(string[] args)
        {           
            using (var system = ActorSystemSignalR.Create("System A", "http://localhost:8080"))
            {
                var actor = system.ActorOf<MyActor>();
                Stopwatch sw = Stopwatch.StartNew();
                for (int i = 0; i < 10; i++)
                {
                    actor.Tell(new TimeRequest());
                }
                Console.WriteLine(sw.Elapsed);
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
            message.Match()
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

    public class LogActor : TypedActor , IHandle<LogMessage> , IHandle<TimeRequest>
    {
        public void Handle(LogMessage message)
        {
            Console.WriteLine("Log {0}", message.Timestamp);
        }

        public void Handle(TimeRequest message)
        {
            Sender.Tell(new TimeResponse
            {
                DateTime = DateTime.Now
            });
        }
    }

    public class MyActor : UntypedActor
    {
        private ActorRef logger;
        public MyActor()
        {
            this.logger = Context.ActorOf<LogActor>();
        }
        
        protected override void OnReceive(object message)
        {
            Console.WriteLine("actor thread: {0}", System.Threading.Thread.CurrentThread.GetHashCode());
            message.Match()
                .With<Greet>(m => Console.WriteLine("Hello {0}", m.Who))
                .With<TimeRequest>(async m => {
                    //TODO: this will execute in another thread, fix
            var result = await Ask(logger, m);
            result.Match()
                .With<TimeResponse>(t => {
                    Console.WriteLine("await thread {0}", System.Threading.Thread.CurrentThread.GetHashCode());
                //     Console.WriteLine("its {0} o'clock", t.DateTime);
                })
                .Default(_ => Console.WriteLine("Unknown message"));

                })
                .Default(m => Console.WriteLine("Unknown message {0}",m));

        //    logger.Tell(new LogMessage(message));
        }
    }
}
