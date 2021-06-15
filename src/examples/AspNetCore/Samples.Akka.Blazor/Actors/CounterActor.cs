using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;

namespace Samples.Akka.Blazor.Actors
{
    public class CounterActor : UntypedActor
    {
        public static Props Props { get; } = Props.Create(() => new CounterActor());

        public sealed class Hit
        {
            public static readonly Hit Instance = new();
            private Hit() { }
        }

        public sealed class Get
        {
            public static readonly Get Instance = new();
            private Get() { }
        }

        private int _count = 0;
        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Hit _:
                {
                    _count++;
                    Sender.Tell(_count);
                    break;
                }
                case Get _:
                {
                    Sender.Tell(Get.Instance);
                    break;
                }
                default:
                    Unhandled(message);
                    break;
            }
        }
    }
}
