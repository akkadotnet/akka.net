using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon
{
    public abstract class ActorRef
    {
        public string Name { get;protected set; }
        public abstract void Tell(IMessage message);
    }
}
