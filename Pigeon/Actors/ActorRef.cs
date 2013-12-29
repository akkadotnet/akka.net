using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon
{
    public abstract class ActorRef
    {
        public abstract void Tell(IMessage message);
    }
}
