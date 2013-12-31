using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public interface IHandle<TMessage> where TMessage : IMessage
    {
        void Handle(TMessage message);
    }

    public abstract class TypedActor : ActorBase
    {
        protected TypedActor(ActorContext context)
            : base(context)
        {
        }
        protected sealed override void OnReceive(IMessage message)
        {
            var method = this.GetType().GetMethod("Handle", new[] { message.GetType() });
            if (method == null)
                throw new ArgumentException("Actor does not handle messages of type " + message.GetType().Name);

            method.Invoke(this, new[] { message });
        }
    }
}
