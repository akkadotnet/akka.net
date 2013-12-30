using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class ActorRefFactory
    {
        public ActorSystem System { get; set; }

        public ActorRef ActorOf<TActor>(string name = null) where TActor : ActorBase
        {
            return this.System.ActorOf<TActor>(name, null);
        }

        public ActorRef ActorSelection(string remoteActorPath)
        {
            /*
 case RelativeActorPath(elems) ⇒
      if (elems.isEmpty) ActorSelection(provider.deadLetters, "")
      else if (elems.head.isEmpty) ActorSelection(provider.rootGuardian, elems.tail)
      else ActorSelection(lookupRoot, elems)
    case ActorPathExtractor(address, elems) ⇒
      ActorSelection(provider.rootGuardianAt(address), elems)
    case _ ⇒
      ActorSelection(provider.deadLetters, "")
             */
            return this.System.ActorSelection(remoteActorPath, null);
        }
    }
}
