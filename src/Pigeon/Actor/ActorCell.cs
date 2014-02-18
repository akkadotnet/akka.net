using Pigeon.Dispatch;
using Pigeon.Dispatch.SysMsg;
using Pigeon.Event;
using Pigeon.Routing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public partial class ActorCell : IActorContext, IActorRefFactory
    {
        
        public virtual ActorSystem System { get;private set; }
        public Props Props { get;private set; }
        public LocalActorRef Self { get; protected set; }
        public InternalActorRef Parent { get; private set; }
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

        //TODO: this is not correct. actor selection should be an object with an anchor actorref and elements
        //resolution of the members is done lazy
        public ActorSelection ActorSelection(ActorPath actorPath)
        {
            var tmpPath = actorPath;
           
            if (System.Provider.Address.Equals( actorPath.Address))
            {
                tmpPath = actorPath;
            }
            else
            {
                var actorRef = System.Provider.ResolveActorRef(actorPath);
                return new ActorSelection(actorPath, actorRef);
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
                    currentContext = currentContext.System.Provider.RootCell;
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
            var uid = GetNextUID();
            name = GetActorName(props, name,uid);
            var path = this.Self.Path / name;
            var actor = System.Provider.ActorOf(System, props, this.Self, name, path, uid);
            this.Children.TryAdd(name, actor);
            return actor;
        }

        private long GetNextUID()
        {
            var auid = Interlocked.Increment(ref uid);
            return auid;
        }

        private static string base64chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789+~";
        private long uid = 0;
        private  string GetActorName(Props props, string name,long uid)
        {            
            var next = uid;
            if (name == null)
            {
                var sb = new StringBuilder("$");

                while(next != 0)
                {
                    var index = (int)(next & 63);
                    var c = base64chars[index];
                    sb.Append(c);
                    next = next >> 6;
                }
                name = sb.ToString();
            }
            return name;
        }

        public void NewActor()
        {
            //set the thread static context or things will break
            this.UseThreadContext( () =>
            {
                //TODO: where should deployment be handled?
                var deployPath = Self.Path.ToStringWithoutAddress();
                var deploy = System.Deployer.Lookup(deployPath);
                behaviorStack.Clear();
                var instance = Props.NewActor();
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

        public ActorCell(ActorSystem system,string name,Mailbox mailbox)
        {
            this.Parent = null;
            
            this.System = system;
            this.Self = new LocalActorRef(new RootActorPath(System.Provider.Address, name), this);
            this.Props = null;
            this.Dispatcher = System.Dispatchers.FromConfig("akka.actor.default-dispatcher");
            mailbox.Setup(this.Dispatcher);
            this.Mailbox = mailbox;
            this.Mailbox.Invoke = this.Invoke;
            this.Mailbox.SystemInvoke = this.SystemInvoke;            
        }

        public ActorCell(ActorSystem system,InternalActorRef supervisor, Props props, string name, Mailbox mailbox)
        {
            this.Parent = supervisor;
            this.System = system;
            this.Self = new LocalActorRef((this.Parent.Path / name), this);
            this.Props = props;
            this.Dispatcher = System.Dispatchers.FromConfig(props.Dispatcher);
            mailbox.Setup(this.Dispatcher);
            this.Mailbox = mailbox;
            this.Mailbox.Invoke = this.Invoke;
            this.Mailbox.SystemInvoke = this.SystemInvoke;
        }

        public void UseThreadContext(Action action)
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
                return;
                //stackoverflow if this is the deadletters actorref
                //this.System.DeadLetters.Tell(new DeadLetter(message, sender, this.Self));
            }

            if (System.Settings.SerializeAllMessages && !(message is NoSerializationVerificationNeeded))
            {
                var serializer = System.Serialization.FindSerializerFor(message);
                var serialized = serializer.ToBinary(message);
                var deserialized = System.Serialization.Deserialize(serialized, serializer.Identifier, message.GetType());
                message = deserialized;
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
