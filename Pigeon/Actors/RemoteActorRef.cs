using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actors
{
    public class RemoteActorRef: ActorRef
    {
        public override void Tell(IMessage message)
        {
            throw new NotImplementedException();
        }
    }
}
