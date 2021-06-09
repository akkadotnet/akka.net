using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Blazor.Tests.Internal
{
    public sealed class CounterActor : ReceiveActor
    {
        public static Props Props { get; } = Props.Create(() => new CounterActor(0));

        public sealed class Hit
        {
            public static readonly Hit Instance = new Hit();
            private Hit(){}
        }

        private int _count;

        public CounterActor(int count)
        {
            _count = count;

            Receive<Hit>(_ =>
            {
                _count++;
                Sender.Tell(_count);
            });
        }
    }
}
