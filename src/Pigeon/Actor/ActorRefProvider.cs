using Pigeon.Dispatch;
using Pigeon.Event;
using Pigeon.Routing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public abstract class ActorRefProvider
    {
        public ActorRefProvider(ActorSystem system)
        {
            this.System = system;            
        }

        public ActorPath RootPath { get;private set; }
        public ActorPath TempNode { get;private set; }
        

        public virtual void Init()
        {
            this.RootPath = new RootActorPath(this.Address,"");
            this.TempNode = RootPath / "temp";

            this.RootCell = new ActorCell(System, "", new ConcurrentQueueMailbox());
            this.DeadLetters = new DeadLetterActorRef(RootPath / "deadLetters", this.System.EventStream);
            this.Guardian = RootCell.ActorOf<GuardianActor>("user");
            this.SystemGuardian = RootCell.ActorOf<GuardianActor>("system");
            this.TempContainer = new VirtualPathContainer(this, TempNode, null);
        }

        public void RegisterTempActor(InternalActorRef actorRef,ActorPath path)
        {
            TempContainer.AddChild(path.Name, actorRef);
        }

        public void UnregisterTempActor(ActorPath path)
        {
            TempContainer.RemoveChild(path.Name);
        }
        public ActorPath TempPath()
        {
            return TempNode / Guid.NewGuid().ToString();
        }

        public VirtualPathContainer TempContainer { get;private set; }
        public ActorSystem System { get;protected set; }
        public ActorCell RootCell { get; protected set; }
        public ActorRef DeadLetters { get; protected set; }
        public LocalActorRef Guardian { get; protected set; }
        public LocalActorRef SystemGuardian { get; protected set; }

        public abstract LocalActorRef ActorOf(ActorSystem system, Props props, InternalActorRef supervisor, ActorPath path, long uid);
        public ActorRef ResolveActorRef(string path){
            var actorPath = ActorPath.Parse(path);
            return ResolveActorRef(actorPath);
        }

        public abstract ActorRef ResolveActorRef(ActorPath actorPath);

        public virtual Address Address
        {
            get;
            set;
        }

        
    }

    public class LocalActorRefProvider : ActorRefProvider
    {
        public LocalActorRefProvider(ActorSystem system) : base(system)
        {
            this.Address = new Address("akka", this.System.Name); //TODO: this should not work this way...
        }

        public override LocalActorRef ActorOf(ActorSystem system, Props props, InternalActorRef supervisor, ActorPath path, long uid)
        {
            var mailbox = System.Mailboxes.FromConfig(props.Mailbox);

            ActorCell cell = null;
            if (props.RouterConfig is NoRouter)
            {
                cell = new ActorCell(system, supervisor, props, path, mailbox);
            }
            else
            {
                cell = new RoutedActorCell(system, supervisor, props, path, mailbox);
            }
            cell.NewActor();

          //  parentContext.Watch(cell.Self);
            return cell.Self;
        }

        public override ActorRef ResolveActorRef(ActorPath actorPath)
        {
            if (this.Address.Equals(actorPath.Address))
            {
                if (actorPath.Head == "temp")
                {
                    //skip ""/"temp", 
                    var parts = actorPath.Skip(2).ToArray();
                    return TempContainer.GetChild(parts);
                }
                else
                {
                    //standard
                    var currentContext = RootCell;
                    foreach (var part in actorPath.Skip(1))
                    {
                        currentContext = ((LocalActorRef)currentContext.Child(part)).Cell;
                    }
                    return currentContext.Self;
                }                
            }
            else
            {
                throw new NotSupportedException("The provided actor path is not valid in the LocalActorRefProvider");
            }
        }
    }
}
