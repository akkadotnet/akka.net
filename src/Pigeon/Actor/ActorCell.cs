using Pigeon.Dispatch;
using Pigeon.Dispatch.SysMsg;
using Pigeon.Routing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public partial class ActorCell : IActorContext, IActorRefFactory
    {
        
        public virtual ActorSystem System { get;private set; }
        public Props Props { get;private set; }
        public LocalActorRef Self { get; protected set; }
        public ActorRef Parent { get; private set; }
        public ActorBase Actor { get;internal set; }
        public object CurrentMessage { get;private set; }
        public ActorRef Sender { get;private set; }
        internal Receive CurrentBehavior { get; private set; }
        private Stack<Receive> behaviorStack = new Stack<Receive>();
        private Mailbox Mailbox { get; set; }
        public MessageDispatcher Dispatcher { get;private set; }
        private HashSet<ActorRef> Watchees = new HashSet<ActorRef>();
        [ThreadStatic]
        private static ActorCell current;
        internal static ActorCell Current
        {
            get
            {
                return current;
            }
        }

        protected ConcurrentDictionary<string, LocalActorRef> Children = new ConcurrentDictionary<string, LocalActorRef>();
        
        public virtual LocalActorRef Child(string name)
        {
            LocalActorRef actorRef = null;
            Children.TryGetValue(name, out actorRef);
            return actorRef;
        }

        public ActorSelection ActorSelection(string path)
        {
            var actorPath = ActorPath.Parse(path,this.System);
            return ActorSelection(actorPath);
        }

        public ActorSelection ActorSelection(ActorPath actorPath)
        {
            var tmpPath = actorPath;
           
            if (actorPath.Address.System == this.System.Name)
            {
                tmpPath = actorPath;
            }
            else
            {
                var actorRef = System.GetRemoteRef(this, actorPath);
                return new ActorSelection(actorPath, actorRef);
            }

            //local absolute
            if (actorPath.First.StartsWith("akka:"))
            {
                tmpPath = new RootActorPath(new Address("akka",this.System.Name), actorPath.Skip(3));
            }

            //standard
            var currentContext = this;
            foreach (var part in tmpPath)
            {
                if (part == "..")
                {
                    currentContext = ((LocalActorRef)currentContext.Parent).Cell;
                }
                else if (part == "." || part == "")
                {
                    currentContext = currentContext.System.RootGuardian.Cell;
                }
                else if (part == "*")
                {
                    var actorRef = new ActorSelection(actorPath, currentContext.Children.Values.ToArray());
                    return actorRef;
                }
                else
                {
                    currentContext = ((LocalActorRef)currentContext.Child(part)).Cell;
                }
            }
            
            return new ActorSelection(actorPath, ((ActorCell)currentContext).Self);
        }

        public virtual LocalActorRef ActorOf<TActor>(string name = null) where TActor : ActorBase
        {
            return ActorOf(Props.Create<TActor>(), name);
        }

        public virtual LocalActorRef ActorOf(Props props, string name = null)
        {
            name = GetActorName(props, name);

            return System.Provider.ActorOf(this, props, name);          
        }

        private static string GetActorName(Props props, string name)
        {
            if (props.Type != null && name == null)
            {
                name = props.Type.Name;
                if (name.EndsWith("Actor"))
                    name = name.Substring(0, name.Length - 5);

                name = name + "#" + Guid.NewGuid();
            }
            return name;
        }

        internal void NewActor(ActorCell cell)
        {
            //set the thread static context or things will break
            cell.UseThreadContext( () =>
            {
                behaviorStack.Clear();
                var instance = cell.Props.NewActor();
                Children.TryAdd(cell.Self.Path.Name, cell.Self);
                instance.AroundPreStart();                
            });
        }

        /// <summary>
        /// May be called from anyone
        /// </summary>
        /// <returns></returns>
        public IEnumerable<LocalActorRef> GetChildren()
        {
            return this.Children.Values.ToArray();
        }

        internal ActorCell(ActorSystem system,string name,Mailbox mailbox)
        {
            this.Parent = null;
            
            this.System = system;
            this.Self = new LocalActorRef(new RootActorPath(new Address("akka",this.System.Name), name), this);
            this.Props = null;
            mailbox.Setup(system.DefaultDispatcher);
            this.Mailbox = mailbox;
            this.Mailbox.Invoke = this.Invoke;
            this.Mailbox.SystemInvoke = this.SystemInvoke;            
        }

        internal ActorCell(IActorContext parentContext, Props props, string name,Mailbox mailbox)
        {
            this.Parent = parentContext != null ? parentContext.Self : null;
            this.System = parentContext != null ? parentContext.System : null;
            this.Self = new LocalActorRef(new ChildActorPath(this.Parent.Path, name), this);
            this.Props = props;
            this.Dispatcher = props.Dispatcher ?? this.System.DefaultDispatcher;
            mailbox.Setup(this.Dispatcher);
            this.Mailbox = mailbox;
            this.Mailbox.Invoke = this.Invoke;
            this.Mailbox.SystemInvoke = this.SystemInvoke;
        }

        internal void UseThreadContext(Action action)
        {
            var tmp = Current;
            current = this;
            try
            {
                action();
            }
            finally
            {
                //ensure we set back the old context
                current = tmp;
            }
        }


        public void Become(Receive receive)
        {
            behaviorStack.Push(receive);
            CurrentBehavior = receive;
        }
        public void Unbecome()
        {
            CurrentBehavior = behaviorStack.Pop(); ;
        }

        internal void Post(ActorRef sender, object message)
        {
            if (Mailbox == null)
            {
                throw new NotSupportedException("Can not post messages to terminated a actor");
            }

            var m = new Envelope
            {
                Sender = sender,
                Message = message,
            };
            Mailbox.Post(m);
        }

        /// <summary>
        /// May only be called from the owner actor
        /// </summary>
        /// <param name="watchee"></param>
        public void Watch(ActorRef watchee)
        {
            Watchees.Add(watchee);
            watchee.Tell(new Watch(watchee,Self));
        }

        /// <summary>
        /// May only be called from the owner actor
        /// </summary>
        /// <param name="watchee"></param>
        public void Unwatch(ActorRef watchee)
        {
            Watchees.Remove(watchee);
            watchee.Tell(new Unwatch(watchee,Self));
        }

        public void Kill()
        {
            Mailbox.Stop();
        }
    }
}
