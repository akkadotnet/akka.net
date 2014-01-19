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
        public Type Type { get; private set; }

        public MessageDispatcher Dispathcer { get;private set; }

        public static Props Create<TActor> (Func<TActor> factory) where TActor : ActorBase
        {
            return new Props(typeof(TActor), factory);
        }
        public static Props Create<TActor>() where TActor : ActorBase
        {
            return new Props(typeof(TActor));
        }
        public static Props Create(Type type)
        {
            return new Props(type);
        }

        private Props(Type type, Func<ActorBase> factory)
        {            
            this.factory = factory;
            this.Type = type;
        }
        private Props(Type type)
        {
            this.factory = () => (ActorBase)Activator.CreateInstance(this.Type, new object[] { });
            this.Type = type;
        }

        public Props WithDispatcher(MessageDispatcher dispatcher)
        {
            this.Dispathcer = dispatcher;
            return this;
        }

        public ActorBase NewActor()
        {
            return this.factory();
        }
    }
}
