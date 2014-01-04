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
        }

        public static void UseThreadContext(ActorContext context, Action action)
        {
            var tmp = Current;
            current = context;
            action();
            current = tmp;
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
            context.Mailbox.OnNext = context.OnNext;

            CreateActor(context);
            return context.Self;
        }

        public ActorBase Actor { get; set; }

        private void OnNext(Message message)
        {
            this.CurrentMessage = message;
            this.Sender = message.Sender;
            //set the current context
            ActorContext.UseThreadContext(this, () =>
            {
                OnReceiveInternal(message.Payload);
            });
        }

        private void OnReceiveInternal(object message)
        {
            try
            {
                Pattern.Match(message)
                    //add watcher
                    .With<Kill>(Kill)
                    .With<PoisonPill>(PoisonPill)
                    .With<Watch>(Watch)
                    .With<Unwatch>(Unwatch)
                    //complete the future callback by setting the result in this thread
                    .With<CompleteFuture>(CompleteFuture)
                    //resolve time distance to actor
                    .With<Ping>(Ping)
                    //supervice exception from child actor
                    .With<SuperviceChild>(SuperviceChild)
                    //handle any other message
                    .Default(Default);
            }
            catch (Exception reason)
            {
                Parent.Self.Tell(new SuperviceChild(reason));
            }
        }

        private void PoisonPill(PoisonPill m)
        {
        }
        private void Kill(Kill m)
        {
            throw new ActorKilledException("Kill");
        }
        private void Default(object m)
        {
            CurrentBehavior(m);
        }

        private void SuperviceChild(SuperviceChild m)
        {
            switch (this.Actor.SupervisorStrategyLazy().Handle(Sender, m.Reason))
            {
                case Directive.Escalate:
                    throw m.Reason;
                case Directive.Resume:
                    break;
                case Directive.Restart:
                    Restart((LocalActorRef)Sender);
                    break;
                case Directive.Stop:
                    Stop((LocalActorRef)Sender);
                    break;
                default:
                    break;
            }
        }

        private void Ping(Ping m)
        {
            Sender.Tell(
                        new Pong
                        {
                            LocalUtcNow = m.LocalUtcNow,
                            RemoteUtcNow = DateTime.UtcNow
                        });
        }

        protected BroadcastActorRef Watchers = new BroadcastActorRef();
        private void Watch(Watch m)
        {
            Watchers.Add(Sender);
        }

        private void Unwatch(Unwatch m)
        {
            Watchers.Remove(Sender);
        }

        private void CompleteFuture(CompleteFuture m)
        {
            m.SetResult();
        }

        private void CreateActor(ActorContext context)
        {
            var prev = ActorContext.Current;
            //set the thread static context or things will break
            ActorContext.UseThreadContext(context, () =>
            {
                Children.TryAdd(context.Self.Path.Name, context.Self);
                var actor = (ActorBase)Activator.CreateInstance(context.Props.Type, new object[] { });
            });
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

        public void Restart(LocalActorRef child)
        {           
            Stop(child);
            Debug.WriteLine("restarting child: {0}", child.Path);
            CreateActor(child.Context);
        }

        public void Stop(LocalActorRef child)
        {
            Debug.WriteLine("stopping child: {0}", child.Path);
            child.Context.Become(m =>
            {
                System.DeadLetters.Tell(m);
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

        internal IEnumerable<LocalActorRef> GetChildren()
        {
            return this.Children.Values.ToArray();
        }

        public void Watch(ActorRef subject)
        {
            subject.Tell(new Watch());
        }

        public void Unwatch(ActorRef subject)
        {
            subject.Tell(new Unwatch());
        }

        public Message CurrentMessage { get; set; }

        public ActorRef Sender { get; set; }
    }    
}
