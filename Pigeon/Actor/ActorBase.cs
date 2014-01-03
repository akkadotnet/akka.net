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
    public abstract class ActorBase : IObserver<Message>
    {
        protected ActorRef Sender { get; private set; }
        protected ActorRef Self { get; private set; }

        protected ActorBase()
        {
            if (ActorContext.Current == null)
                throw new Exception("Do not create actors using 'new', always create them using an ActorContext/System");

            Context.Self.SetActor(this);
            this.Self = Context.Self;
            Context.Mailbox.AsObservable().Subscribe(this);
        }

        private void OnReceiveInternal(object message)
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
                //handle any other message
                .Default(m => OnReceive(m));
        }

        protected abstract void OnReceive(object message);

        

        void IObserver<Message>.OnCompleted()
        {
        }

        void IObserver<Message>.OnError(Exception error)
        {
        }

        void IObserver<Message>.OnNext(Message value)
        {
            this.Sender = value.Sender;
            //set the current context
            ActorContext.Current = value.Target.Context;
            OnReceiveInternal(value.Payload);
            //clear the current context
            ActorContext.Current = null;
        }

        public void Tell(ActorRef actor, object message)
        {
            actor.Tell(message, Self);
        }

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
    }    
}
