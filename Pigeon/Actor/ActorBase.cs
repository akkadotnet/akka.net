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
        protected ActorRef Sender
        {
            get
            {
                return Context.Sender;
            }
        }
        protected LocalActorRef Self { get; private set; }

       

        protected ActorBase()
        {
            if (ActorContext.Current == null)
                throw new Exception("Do not create actors using 'new', always create them using an ActorContext/System");
            Context.Become(OnReceive);
            Context.Actor = this;
            this.Self = Context.Self;            
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
