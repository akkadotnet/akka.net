using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Serialization;
using Akka.Tools;

namespace Akka.Actor
{
    public partial class ActorCell : IActorContext, IUntypedActorContext
    {
        private const string Base64Chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789+~";
        [ThreadStatic] private static ActorCell current;

        protected ConcurrentDictionary<string, InternalActorRef> children =
            new ConcurrentDictionary<string, InternalActorRef>();

        protected HashSet<ActorRef> watchees = new HashSet<ActorRef>();
        protected Stack<Receive> behaviorStack = new Stack<Receive>();
        private long uid;

        public ActorCell(ActorSystem system, string name, Mailbox mailbox)
        {
            Parent = null;

            System = system;
            Self = new LocalActorRef(new RootActorPath(System.Provider.Address, name), this);
            Props = null;
            Dispatcher = System.Dispatchers.FromConfig("akka.actor.default-dispatcher");
            mailbox.Setup(Dispatcher);
            Mailbox = mailbox;
            Mailbox.ActorCell = this;
        }

        public ActorCell(ActorSystem system, InternalActorRef supervisor, Props props, ActorPath path, Mailbox mailbox)
        {
            Parent = supervisor;
            System = system;
            Self = new LocalActorRef(path, this);
            Props = props;
            Dispatcher = System.Dispatchers.FromConfig(props.Dispatcher);
            mailbox.Setup(Dispatcher);
            Mailbox = mailbox;
            Mailbox.ActorCell = this;
        }

        public ActorBase Actor { get; internal set; }
        public object CurrentMessage { get; private set; }
        public Mailbox Mailbox { get; protected set; }
        public MessageDispatcher Dispatcher { get; private set; }

        internal static ActorCell Current
        {
            get { return current; }
        }

        public ActorSystem System { get; private set; }
        public Props Props { get; private set; }
        public LocalActorRef Self { get; protected set; }
        public InternalActorRef Parent { get; private set; }
        public ActorRef Sender { get; private set; }


        public virtual InternalActorRef Child(string name)
        {
            InternalActorRef actorRef;
            children.TryGetValue(name, out actorRef);
            if (actorRef.IsNobody())
                return ActorRef.Nobody;
            return actorRef;
        }

        public ActorSelection ActorSelection(string path)
        {
            if (Uri.IsWellFormedUriString(path, UriKind.Absolute))
            {
                ActorPath actorPath = ActorPath.Parse(path);
                ActorRef actorRef = System.Provider.RootGuardianAt(actorPath.Address);
                return new ActorSelection(actorRef, actorPath.Elements.ToArray());
            }
            //no path given
            if (string.IsNullOrEmpty(path))
            {
                return new ActorSelection(System.DeadLetters, "");
            }

            //absolute path
            if (path.Split('/').First() == "")
            {
                return new ActorSelection(System.Provider.RootCell.Self, path.TrimStart('/'));
            }

            return new ActorSelection(Self, path);
        }

        public ActorSelection ActorSelection(ActorPath actorPath)
        {
            ActorRef actorRef = System.Provider.ResolveActorRef(actorPath);
            return new ActorSelection(actorRef, "");
        }

        public virtual InternalActorRef ActorOf<TActor>(string name = null) where TActor : ActorBase
        {
            return ActorOf(Props.Create<TActor>(), name);
        }

        public virtual InternalActorRef ActorOf(Props props, string name = null)
        {
            return MakeChild(props, name);
        }

        /// <summary>
        ///     May be called from anyone
        /// </summary>
        /// <returns></returns>
        public IEnumerable<InternalActorRef> GetChildren()
        {
            return children.Values.ToArray();
        }


        public void Become(Receive receive, bool discardOld = true)
        {
            if(discardOld && behaviorStack.Count > 1) //We should never pop off the initial receiver
                behaviorStack.Pop();
            behaviorStack.Push(receive);
        }

        public void Unbecome()
        {
            if(behaviorStack.Count>1)   //We should never pop off the initial receiver
                behaviorStack.Pop();
        }

        void IUntypedActorContext.Become(UntypedReceive receive, bool discardOld)
        {
            Become(m => { receive(m); return true; }, discardOld);
        }

        /// <summary>
        ///     May only be called from the owner actor
        /// </summary>
        /// <param name="watchee"></param>
        public void Watch(ActorRef watchee)
        {
            watchees.Add(watchee);
            watchee.Tell(new Watch(watchee, Self),Self);
        }

        /// <summary>
        ///     May only be called from the owner actor
        /// </summary>
        /// <param name="watchee"></param>
        public void Unwatch(ActorRef watchee)
        {
            watchees.Remove(watchee);
            watchee.Tell(new Unwatch(watchee, Self));
        }

        private InternalActorRef MakeChild(Props props, string name)
        {
            long childUid = NewUid();
            name = GetActorName(name, childUid);
            //reserve the name before we create the actor
            ReserveChild(name);
            try
            {
                ActorPath childPath = (Self.Path/name).WithUid(childUid);
                InternalActorRef actor = System.Provider.ActorOf(System, props, Self, childPath);
                //replace the reservation with the real actor
                InitChild(name, actor);
                return actor;
            }
            catch
            {
                //if actor creation failed, unreserve the name
                UnreserveChild(name);
                throw;
            }
        }

        private void UnreserveChild(string name)
        {
            InternalActorRef tmp;
            children.TryRemove(name, out tmp);
        }

        private void InitChild(string name, InternalActorRef actor)
        {
            children.TryUpdate(name, actor, ActorRef.Reserved);
        }

        private void ReserveChild(string name)
        {
            if (!children.TryAdd(name, ActorRef.Reserved))
            {
                throw new Exception("The name is already reserved: " + name);
            }
        }

        private long NewUid()
        {
            long auid = Interlocked.Increment(ref uid);
            return auid;
        }

        private string GetActorName(string name, long actorUid)
        {
            long next = actorUid;
            if (name == null)
            {
                var sb = new StringBuilder("$");

                while (next != 0)
                {
                    var index = (int) (next & 63);
                    char c = Base64Chars[index];
                    sb.Append(c);
                    next = next >> 6;
                }
                name = sb.ToString();
            }
            return name;
        }

        public virtual void NewActor()
        {
            //set the thread static context or things will break
            UseThreadContext(() =>
            {
                behaviorStack.Clear();
                ActorBase instance = Props.NewActor();
                instance.supervisorStrategy = Props.SupervisorStrategy;
                //defaults to null - won't affect lazy instantion unless explicitly set in props
                instance.AroundPreStart();
            });
        }

        public void UseThreadContext(Action action)
        {
            ActorCell tmp = Current;
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


        internal virtual void Post(ActorRef sender, object message)
        {
            if (Mailbox == null)
            {
                return;
                //stackoverflow if this is the deadletters actorref
                //this.System.DeadLetters.Tell(new DeadLetter(message, sender, this.Self));
            }

            if (System.Settings.SerializeAllMessages && !(message is NoSerializationVerificationNeeded))
            {
                Serializer serializer = System.Serialization.FindSerializerFor(message);
                byte[] serialized = serializer.ToBinary(message);
                object deserialized = System.Serialization.Deserialize(serialized, serializer.Identifier,
                    message.GetType());
                message = deserialized;
            }
            
            //Execute CompleteFuture objects inline - if the Actor is waiting on the result of an Ask operation inside
            //its receive method, then the mailbox will never schedule the CompleteFuture.
            //Thus - we execute it inline, outside of the mailbox.
            if (message is CompleteFuture)
            {
                HandleCompleteFuture(message.AsInstanceOf<CompleteFuture>());
                return;
            }

            var m = new Envelope
            {
                Sender = sender,
                Message = message,
            };

            Mailbox.Post(m);
        }
    }
}