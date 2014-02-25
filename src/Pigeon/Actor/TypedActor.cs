using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Actor
{
    public interface IHandle<TMessage>
    {
        void Handle(TMessage message);
    }

    public abstract class TypedActor : ActorBase
    {
        protected sealed override void OnReceive(object message)
        {
            var method = this.GetType().GetMethod("Handle", new[] { message.GetType() });
            if (method == null)
            {
                Unhandled(message);
                return;
            }

            method.Invoke(this, new[] { message });
        }
    }
}
