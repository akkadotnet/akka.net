using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared
{
    public class SomeActor : UntypedActor
    {
        public SomeActor(string someArg, long otherArg)
        {
            Console.WriteLine("Constructuing SomeActor with {0},{1}",someArg,otherArg);
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
