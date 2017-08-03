using System;
using System.Diagnostics;
using Akka.Actor;

namespace SpawnBenchmark
{
    public sealed class RootActor : UntypedActor
    {
        public class Run
        {
            public Run(int number)
            {
                Number = number;
            }

            public int Number { get; }
        }

        private static readonly Stopwatch Stopwatch = Stopwatch.StartNew();

        protected override void OnReceive(object message)
        {
            if (message is Run run)
            {
                StartRun(run.Number);
            }
        }

        private void StartRun(int n)
        {
            Console.WriteLine($"Start run {n}");

            var start = Stopwatch.ElapsedMilliseconds;
            Context.ActorOf(SpawnActor.Props).Tell(new SpawnActor.Start(7, 0));
            Context.Become(Waiting(n - 1, start));
        }

        private UntypedReceive Waiting(int n, long start)
        {
            return message => 
            {
                if (message is long x)
                {
                    var diff = (Stopwatch.ElapsedMilliseconds - start);
                    Console.WriteLine($"Run {n + 1} result: {x} in {diff} ms");
                    if (n == 0)
                    {
                        Context.System.Terminate();
                    }
                    else
                    {
                        StartRun(n);
                    }
                }
            };
        }

        public static Props Props { get; } = Props.Create<RootActor>();
    }
}