using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Pigeon.Dispatch.SysMsg;

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
            if (ActorCell.Current == null)
                throw new Exception("Do not create actors using 'new', always create them using an ActorContext/System");
            Context.Become(OnReceive);
            ((ActorCell)Context).Actor = this;
            this.Self = Context.Self;
            ((ActorCell)Context).Start();
        }

        protected abstract void OnReceive(object message);

        protected void Unhandled(object message)
        {
            Context.System.EventStream.Tell(new UnhandledMessage(message, Sender, Self));
        }

        protected static IActorContext Context
        {
            get
            {
                var context = ActorCell.Current;
                if (context == null)
                    throw new NotSupportedException("There is no active ActorContext, this is most likely due to use of async operations from within this actor.");

                return context;
            }
        }
        protected void Become(Receive receive)
        {
            Context.Become(receive);
        }

        protected void Unbecome()
        {
            Context.Unbecome();
        }
    }    
}
