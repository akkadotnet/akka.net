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
        private BufferBlock<Message> messages = new BufferBlock<Message>(new DataflowBlockOptions()
        {
            BoundedCapacity = 100,
            TaskScheduler = TaskScheduler.Default,
        });


        protected ActorRef Sender { get; private set; }
        protected ActorRef Self { get; private set; }

        protected ActorBase()
        {
            if (ActorContext.Current == null)
                throw new Exception("Do not create actors using 'new', always create them using an ActorContext/System");

            this.Context.Self.SetActor(this);
            this.Self = Context.Self;
            messages.AsObservable().Subscribe(this);
        }

        private void OnReceiveInternal(IMessage message)
        {           
            message.Match()
                .With<ActorAction>(m => m.Action())
                .Default(m => OnReceive(m));
        }

        protected abstract void OnReceive(IMessage message);


        internal void Post(ActorRef sender,LocalActorRef target, IMessage message)
        {
            var m = new Message
            {
                Sender = sender,
                Target = target,
                Payload = message,
            };
            messages.SendAsync(m);
        }

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

        public void Tell(ActorRef actor, IMessage message)
        {
            actor.Tell(message, Self);
        }

        public Task<IMessage> Ask(ActorRef actor, IMessage message)
        {
            var result = new TaskCompletionSource<IMessage>();            
            var future = Context.ActorOf<FutureActor>();
            future.Tell(new SetRespondTo { Result = result }, Self);
            actor.Tell(message, future);
            return result.Task;
        }

        protected ActorContext Context
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

    public class ActorAction : IMessage
    {
        public Action Action { get; set; }
    }
}
