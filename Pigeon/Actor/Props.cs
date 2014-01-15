using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class Props
    {
        private Func<ActorBase> factory;

        public static Props Factory<TActor> (Func<TActor> factory) where TActor : ActorBase
        {
            return new Props(typeof(TActor), factory);
        }
        public Props(Type type, Func<ActorBase> factory)
        {            
            this.factory = factory;
            this.Type = type;
        }
        public Props(Type type)
        {
            this.factory = () => (ActorBase)Activator.CreateInstance(this.Type, new object[] { });
            this.Type = type;
        }

        public Type Type { get;private set; }

        public ActorBase NewActor()
        {
            return this.factory();
        }
    }
}
