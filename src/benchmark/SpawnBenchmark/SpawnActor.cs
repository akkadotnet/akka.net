//-----------------------------------------------------------------------
// <copyright file="SpawnActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace SpawnBenchmark
{
    public sealed class SpawnActor : UntypedActor
    {
        public class Start
        {
            public Start(int level, long number)
            {
                Level = level;
                Number = number;
            }

            public int Level { get; }

            public long Number { get; }
        }

        private int _todo = 10;
        private long _count = 0L;

        protected override void OnReceive(object message)
        {
            if (message is Start start)
            {
                if (start.Level == 1)
                {
                    Context.Parent.Tell(start.Number);
                    Context.Stop(Self);
                }
                else
                {
                    var startNumber = start.Number * 10;

                    for (int i = 0; i <= 9; i++)
                    {
                        Context.ActorOf(Props).Tell(new Start(start.Level - 1, startNumber + i));
                    }
                }
            }
            else if (message is long l)
            {
                _todo -= 1;
                _count += l;
                if (_todo == 0)
                {
                    Context.Parent.Tell(_count);
                    Context.Stop(Self);
                }
            }
        }

        public static Props Props { get; } = Props.Create<SpawnActor>();
    }
}
