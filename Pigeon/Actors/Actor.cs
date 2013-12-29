using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Reactive.Linq;

namespace Pigeon
{
    public interface IHandle<TMessage> where TMessage : IMessage
    {
        void Handle(TMessage message);
    }

    public abstract class TypedActor : ActorBase , IHandle<IMessage>
    {
        protected sealed override void OnReceive(IMessage message)
        {
            this.Handle(message);
        }

        public void Handle(IMessage message)
        {
            var method = this.GetType().GetMethod("Handle", new[] { message.GetType() });
            if (method == null)
                throw new ArgumentException("Actor does not handle messages of type " + message.GetType().Name);

            method.Invoke(this, new [] {message});
        }
    }

    public abstract class UntypedActor : ActorBase
    {
        public UntypedActor()
            : base()
        {
        }

    }

    public abstract class ActorBase : IObserver<IMessage>
    {
        protected static Context Context = new Context();
        private BufferBlock<IMessage> messages = new BufferBlock<IMessage>(new DataflowBlockOptions()
        {
            BoundedCapacity = 100,
            TaskScheduler = TaskScheduler.Default,
        });

        protected ActorBase()
        {
            messages.AsObservable().Subscribe(this);
        }
        protected abstract void OnReceive(IMessage message);

        public void Tell(IMessage message)
        {
            messages.SendAsync(message);
        }

        void IObserver<IMessage>.OnCompleted()
        {
        }

        void IObserver<IMessage>.OnError(Exception error)
        {
        }

        void IObserver<IMessage>.OnNext(IMessage value)
        {
            OnReceive(value);
        }
    }
}
