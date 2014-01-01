using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{

    public class ActorContext : ActorRefFactory
    {
        [ThreadStatic]
        private static ActorContext current;
        internal static ActorContext Current
        {
            get
            {
                return current;
            }
            set
            {
                current = value;
            }
        }
        
        public LocalActorRef Self { get;  set; }
        public ActorRefFactory Parent { get; set; }

        protected ConcurrentDictionary<string, ActorRef> Children = new ConcurrentDictionary<string,ActorRef>();

        public ActorRef ActorFor(string name)
        {
            return Child(name); 
        }

        public override ActorRef ActorOf<TActor>(string name = null) 
        {
            if (name == null)
            {
                name = typeof(TActor).Name;
                if (name.EndsWith("Actor"))
                    name = name.Substring(0, name.Length - 5);

                name = name + "#" + Guid.NewGuid();
            }          

            var context = new ActorContext();            
            context.Parent = this;
            context.System = this.System;
            context.Self = new LocalActorRef(new ActorPath(name), context);
            //set the thread static context or things will break
            ActorContext.Current = context;
            Children.TryAdd(name, context.Self);
            var actor = (ActorBase)Activator.CreateInstance(typeof(TActor), new object[] {});
            ActorContext.Current = null;
            return context.Self;
        }
        public override ActorRef Child(string name)
        {
            ActorRef actorRef = null;
            Children.TryGetValue(name, out actorRef);
            return actorRef;
        }

        public override ActorRef ActorSelection(string remoteActorPath)
        {
            var actorRef = new RemoteActorRef(this, remoteActorPath);
            return actorRef;
        }

        public override void Stop(ActorRef actor)
        {
            ActorRef value = null;
            Children.TryRemove(actor.Path.Name, out value);
        }
    }    
}
