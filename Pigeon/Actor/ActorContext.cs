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
        public Mailbox Mailbox { get; private set; }
        public Props Props { get; private set; }
        public LocalActorRef Self { get; private set; }
        public ActorContext Parent { get;private set; }

        protected ConcurrentDictionary<string, ActorRef> Children = new ConcurrentDictionary<string,ActorRef>();
        
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
            context.Mailbox = new BufferBlockMailbox();

            ActorOfInternal(context);
            return context.Self;
        }        

        private void ActorOfInternal(ActorContext context)
        {
            //set the thread static context or things will break
            ActorContext.Current = context;
            Children.TryAdd(context.Self.Path.Name, context.Self);
            var actor = (ActorBase)Activator.CreateInstance(context.Props.Type, new object[] { });
            ActorContext.Current = null;
        }

        internal void Post(ActorRef sender, LocalActorRef target, object message)
        {
            var m = new Message
            {
                Sender = sender,
                Target = target,
                Payload = message,
            };
            Mailbox.Post(m);
        }

        public void Stop()
        {
            this.Parent.StopChild(this);
        }

        public void Restart()
        {
            this.Parent.RestartChild(this);
        }

        private void RestartChild(ActorContext actorContext)
        {
            StopChild(actorContext);
            ActorOfInternal(actorContext);
        }

        private void StopChild(ActorContext actorContext)
        {
            ActorRef tmp;
            var name = actorContext.Self.Path.Name;
            this.Children.TryRemove(name, out tmp);           
        }

        public Action<object> CurrentBehavior { get; private set; }
        private Stack<Action<object>> behaviorStack = new Stack<Action<object>>();
        public void Become(Action<object> receive)
        {
            behaviorStack.Push(receive);
            CurrentBehavior = receive;
        }

        public void Unbecome()
        {
            CurrentBehavior = behaviorStack.Pop(); ;
        }        
    }    
}
