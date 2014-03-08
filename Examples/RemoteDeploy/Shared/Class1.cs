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
        protected override void OnReceive(object message)
        {
            Console.WriteLine("SomeActor got {0}", message);
            Sender.Tell("hello");
        }
    }
}
