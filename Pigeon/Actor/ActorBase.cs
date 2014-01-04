using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Reactive.Linq;
using Pigeon.Messaging;
using System.Threading;

namespace Pigeon.Actor
{   
    public abstract partial class ActorBase
    {
        protected ActorRef Sender { get; private set; }
        protected ActorRef Self { get; private set; }

        protected ActorBase()
        {
            if (ActorContext.Current == null)
                throw new Exception("Do not create actors using 'new', always create them using an ActorContext/System");
            Context.Become(OnReceive);
            Context.Self.SetActor(this);
            this.Self = Context.Self;
            Context.Mailbox.OnNext = message =>
                {
                    this.Sender = message.Sender;
                    //set the current context
                    ActorContext.Current = message.Target.Context;
                    OnReceiveInternal(message.Payload);
                    //clear the current context
                    ActorContext.Current = null;
                };
        }

        private void OnReceiveInternal(object message)
        {
            try
            {
                Pattern.Match(message)
                    //execute async callbacks within the actor thread
                    .With<ActorAction>(m => m.Action())
                    //resolve time distance to actor
                    .With<Ping>(m => Sender.Tell(
                        new Pong
                        {
                            LocalUtcNow = m.LocalUtcNow,
                            RemoteUtcNow = DateTime.UtcNow
                        }))
                    .With<SuperviceChild>(m =>
                    {
                        this.SupervisorStrategyLazy().Handle(Sender, m.Reason);
                    })
                    //handle any other message
                    .Default(m => Context.CurrentBehavior(m));
            }
            catch (Exception reason)
            {
                Context.Parent.Self.Tell(new SuperviceChild(reason));
            }
        }

       

        protected abstract void OnReceive(object message);

        protected void Unhandled(object message)
        {
            Context.System.EventStream.Tell(new UnhandledMessage(message, Sender, Self));
        }
        protected static ActorContext Context
        {
            get
            {
                var context = ActorContext.Current;
                if (context == null)
                    throw new NotSupportedException("There is no active ActorContext, this is most likely due to use of async operations from within this actor.");

                return context;
            }
        }

        protected void Become(MessageHandler receive)
        {
            Context.Become(receive);
        }

        protected void Unbecome()
        {
            Context.Unbecome();
        }
    }    
}
