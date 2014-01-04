using Pigeon.Messaging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
        public LocalActorRef Self { get; protected set; }
        public ActorContext Parent { get;private set; }

        protected ConcurrentDictionary<string, LocalActorRef> Children = new ConcurrentDictionary<string, LocalActorRef>();

        public override LocalActorRef Child(string name)
        {
            LocalActorRef actorRef = null;
            Children.TryGetValue(name, out actorRef);
            return actorRef;
        }

        public override ActorRef ActorSelection(string actorPath)
        {
            return ActorSelection(new ActorPath(actorPath));
        }

        public ActorRef ActorSelection(ActorPath actorPath)
        {
            //remote path
            if (actorPath.First.StartsWith("pigeon."))
            {
                var actorRef = new RemoteActorRef(this, actorPath);
                return actorRef;
            }

            //local absolute
            if (actorPath.First.StartsWith("pigeon:"))
            {
                actorPath = new ActorPath(actorPath.Skip(1));
            }

            //standard
            var currentContext = this;
            foreach (var part in actorPath)
            {
                if (part == "..")
                {
                    currentContext = currentContext.Parent;
                }
                else if (part == "." || part == "")
                {
                    currentContext = currentContext.System;
                }
                else if (part == "*")
                {
                    var actorRef = new BroadcastActorRef(currentContext.Children.Values.ToArray());
                    return actorRef;
                }
                else
                {
                    currentContext = ((LocalActorRef)this.Child(part)).Context;
                }
            }
            
            return currentContext.Self;
        }

        public override LocalActorRef ActorOf<TActor>(string name = null)
        {
            return ActorOf(new Props(typeof(TActor)), name);
        }

        public override LocalActorRef ActorOf(Props props, string name = null)
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
            var prev = ActorContext.Current;
            //set the thread static context or things will break
            ActorContext.Current = context;
            Children.TryAdd(context.Self.Path.Name, context.Self);
            var actor = (ActorBase)Activator.CreateInstance(context.Props.Type, new object[] { });
            ActorContext.Current = prev;
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

        //public void Stop()
        //{
        //    foreach (var child in Children.Values)
        //        child.Tell(new Stop());

        //    this.Become(m =>
        //    {
        //        System.Deadletters.Tell(m);
        //    });

        //    this.Parent.StopChild(this);
        //}



        public void Restart(LocalActorRef child)
        {           
            Stop(child);
            Debug.WriteLine("restarting child: {0}", child.Path);
            ActorOfInternal(child.Context);
        }

        public void Stop(LocalActorRef child)
        {
            Debug.WriteLine("stopping child: {0}", child.Path);
            child.Context.Become(m =>
            {
                System.Deadletters.Tell(m);
            });
            LocalActorRef tmp;
            var name = child.Path.Name;
            this.Children.TryRemove(name, out tmp);           
        }

        public MessageHandler CurrentBehavior { get; private set; }
        private Stack<MessageHandler> behaviorStack = new Stack<MessageHandler>();
        public void Become(MessageHandler receive)
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
