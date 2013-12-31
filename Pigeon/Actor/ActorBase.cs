using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Reactive.Linq;
using Pigeon.Messaging;

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

        protected ActorBase(ActorContext context)
        {
            this.Context = context;
            this.Context.Self.SetActor(this);
            this.Self = context.Self;
            messages.AsObservable().Subscribe(this);
        }

        protected abstract void OnReceive(IMessage message);

        public void Tell(ActorRef sender, IMessage message)
        {
            var m = new Message
            {
                Sender = sender,
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
            //this.Sender = this.Context.Self;
            OnReceive(value.Payload);
        }

        protected ActorContext Context { get;private set; }      
    }
}
