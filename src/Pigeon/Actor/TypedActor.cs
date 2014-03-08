using System.Reflection;

namespace Akka.Actor
{
    public interface IHandle<TMessage>
    {
        void Handle(TMessage message);
    }

    public abstract class TypedActor : ActorBase
    {
        protected override sealed void OnReceive(object message)
        {
            MethodInfo method = GetType().GetMethod("Handle", new[] {message.GetType()});
            if (method == null)
            {
                Unhandled(message);
                return;
            }

            method.Invoke(this, new[] {message});
        }
    }
}