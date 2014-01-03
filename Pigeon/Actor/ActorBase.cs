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
    public abstract class ActorBase
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
            catch (Exception x)
            {
                Context.Parent.Self.Tell(new SuperviceChild
                {
                    Reason = x,
                });
            }
        }

        private SupervisorStrategy supervisorStrategy = null;
        private SupervisorStrategy SupervisorStrategyLazy()
        {
            if (supervisorStrategy == null)
                supervisorStrategy = SupervisorStrategy();

            return supervisorStrategy;
        }
        protected virtual SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(10, TimeSpan.FromSeconds(30), OneForOneStrategy.DefaultDecider);
        }

        protected abstract void OnReceive(object message);

        public Task<object> Ask(ActorRef actor, object message)
        {
            var result = new TaskCompletionSource<object>();            
            var future = Context.ActorOf<FutureActor>();
            future.Tell(new SetRespondTo { Result = result }, Self);
            actor.Tell(message, future);
            return result.Task;
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
