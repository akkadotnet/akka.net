using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class ActorContext : ActorRefFactory
    {        
        public string Name { get; set; }

        public ActorRef Self { get; private set; }
        public ActorRef Parent { get;private set; }

        private IEnumerable<ActorRef> Children { get; set; }
        public ActorRef Child(string name)
        {
            return Children.Where(actorRef => actorRef.Path == name).FirstOrDefault();
        }
    }    
}
