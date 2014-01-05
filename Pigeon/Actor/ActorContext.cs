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

    public partial class ActorContext : ActorRefFactory
    {
        [ThreadStatic]
        private static ActorCell current;
        internal static ActorCell Current
        {
            get
            {
                return current;
            }
        }

        public static void UseThreadContext(ActorCell context, Action action)
        {
            var tmp = Current;
            current = context;
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
                    currentContext = ((LocalActorRef)this.Child(part)).Cell;
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

            var cell = new ActorCell();
            cell.Parent = this;
            cell.System = this.System;
            cell.Self = new LocalActorRef(new ActorPath(name), cell);
            cell.Props = props;
            cell.Mailbox = new BufferBlockMailbox();
            cell.Mailbox.OnNext = cell.OnNext;

            CreateActor(cell);
            return cell.Self;
        }

        protected void CreateActor(ActorCell cell)
        {
            var prev = ActorContext.Current;
            //set the thread static context or things will break
            ActorContext.UseThreadContext(cell, () =>
            {
                Children.TryAdd(cell.Self.Path.Name, cell.Self);
                var actor = (ActorBase)Activator.CreateInstance(cell.Props.Type, new object[] { });
            });
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
    }    
}
