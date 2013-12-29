using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks.Dataflow;

namespace Pigeon
{
    public class Context
    {
        public static ActorRef GetActor<TActor>() where TActor : ActorBase, new()
        {
            return new LocalActorRef(new TActor());
        }
    }
}
