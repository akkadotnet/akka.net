using Pigeon.Messaging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

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
        public BufferBlock<Message> Mailbox { get; private set; }
        public Props Props { get; private set; }
        public LocalActorRef Self { get;  set; }
        public ActorRefFactory Parent { get; set; }

        protected ConcurrentDictionary<string, ActorRef> Children = new ConcurrentDictionary<string,ActorRef>();

        public ActorRef ActorFor(string name)
        {
            return Child(name); 
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

        public override ActorRef ActorOf<TActor>(string name = null)
        {
            return ActorOf(new Props(typeof(TActor)), name);
        }

        public override ActorRef ActorOf(Props props, string name = null)
        {
            if (name == null)
            {
                name = props.Type.Name;
                if (name.EndsWith("Actor"))
                    name = name.Substring(0, name.Length - 5);

                name = name + "#" + Guid.NewGuid();
            }

            var context = new ActorContext();
            context.Parent = this;
            context.System = this.System;
            context.Self = new LocalActorRef(new ActorPath(name), context);
            context.Props = props;
            context.Mailbox = new BufferBlock<Message>(new DataflowBlockOptions()
            {
                BoundedCapacity = 100,
                TaskScheduler = TaskScheduler.Default,
            });

            //set the thread static context or things will break
            ActorContext.Current = context;
            Children.TryAdd(name, context.Self);
            var actor = (ActorBase)Activator.CreateInstance(props.Type, new object[] { });
            ActorContext.Current = null;
            return context.Self;
        }

        internal void Post(ActorRef sender, LocalActorRef target, object message)
        {
            var m = new Message
            {
                Sender = sender,
                Target = target,
                Payload = message,
            };
            Mailbox.SendAsync(m);
        }
    }    
}
