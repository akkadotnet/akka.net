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
        public virtual void Init()
        {
            this.RootCell = new ActorCell(System, "", new ConcurrentQueueMailbox());
            this.DeadLetters = new DeadLetterActorRef(ActorPath.Parse("deadLetters", System), this.System.EventStream);
            this.Guardian = RootCell.ActorOf<GuardianActor>("user");
            this.SystemGuardian = RootCell.ActorOf<GuardianActor>("system");
            this.TempGuardian = RootCell.ActorOf<GuardianActor>("temp");        
        }
        public ActorSystem System { get;protected set; }
        public ActorCell RootCell { get; protected set; }
        public LocalActorRef RootGuardian { get; protected set; }

        public ActorRef DeadLetters { get; protected set; }
        public LocalActorRef Guardian { get; protected set; }
        public LocalActorRef SystemGuardian { get; protected set; }
        public LocalActorRef TempGuardian { get; protected set; }      

        public abstract LocalActorRef ActorOf(ActorCell parentContext,Props props,string name);
    }

    public class LocalActorRefProvider : ActorRefProvider
    {
        public LocalActorRefProvider(ActorSystem system) : base(system)
        {
        }

        public override LocalActorRef ActorOf(ActorCell parentContext, Props props, string name)
        {
            var mailbox = (Mailbox)Activator.CreateInstance(props.MailboxType);

            if (props.RouterConfig != null)
            {
                var cell = new RoutedActorCell(parentContext, props, name, mailbox);
                parentContext.NewActor(cell);
                parentContext.Watch(cell.Self);
                return cell.Self;
            }
            else
            {
                var cell = new ActorCell(parentContext, props, name, mailbox);
                parentContext.NewActor(cell);
                parentContext.Watch(cell.Self);
                return cell.Self;
            }
        }
    }
}
