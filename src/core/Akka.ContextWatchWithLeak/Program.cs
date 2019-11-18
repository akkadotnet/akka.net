using System;
using System.Collections.Generic;
using Akka.Actor;

namespace MemoryLeakRepro
{
    public class LoadHandler : ReceiveActor
    {
        private readonly List<IActorRef> _subjects;
        private readonly ICancelable _cancel;

        public LoadHandler()
        {
            _subjects = new List<IActorRef>();
            _cancel = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                initialDelay: TimeSpan.FromSeconds(1),
                interval: TimeSpan.FromSeconds(1),
                receiver: Self,
                message: Iteration.Instance,
                sender: ActorRefs.NoSender);

            Receive<Iteration>(
                _ =>
                {
                    // stop actors created on previous iteration
                    _subjects.ForEach(Context.Stop);
                    _subjects.Clear();

                    // create a set of actors and start watching them
                    for (var i = 0; i < 10_000; i++)
                    {
                        var subject = Context.ActorOf(Props.Create<Subject>());
                        _subjects.Add(subject);
                        Context.WatchWith(subject, new Stopped(subject));
                    }
                });

            Receive<Stopped>(_ => { });
        }

        private class Iteration
        {
            public static readonly Iteration Instance = new Iteration();
            private Iteration() { }
        }

        private class Stopped
        {
            public IActorRef ActorRef { get; }

            public Stopped(IActorRef actorRef)
            {
                ActorRef = actorRef;
            }
        }

        private class Subject : ReceiveActor
        {
            // simulate internal state
            private byte[] _state = new byte[1000];
        }

        protected override void PostStop() => _cancel.Cancel();
    }

    public static class Program
    {
        public static void Main(string[] args)
        {
            using (var actorSystem = ActorSystem.Create("repro"))
            {
                actorSystem.ActorOf(Props.Create<LoadHandler>());
                Console.ReadKey();
            }
        }
    }
}