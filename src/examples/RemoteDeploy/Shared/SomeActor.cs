using System;
using Akka.Actor;

namespace Shared
{
    public class SomeActor : UntypedActor
    {
        public SomeActor(string someArg, long otherArg)
        {
            Console.WriteLine("Constructing SomeActor with {0},{1}", someArg, otherArg);
        }

        protected override void OnReceive(object message)
        {
            if (message is long)
            {
                Console.Write(".");
            }
            else
            {
                Console.WriteLine("{0} got {1}", Self.Path.ToStringWithAddress(), message);
                Sender.Tell("hello");
            }
        }
    }
}