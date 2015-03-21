using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Actor
{
    public interface ICanTell
    {
        void Tell(object message, ActorRef sender);
    }
}
