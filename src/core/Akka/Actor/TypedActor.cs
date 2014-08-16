using System;
using System.Reflection;

namespace Akka.Actor
{
    /// <summary>
    ///     Interface IHandle
    /// </summary>
    /// <typeparam name="TMessage">The type of the t message.</typeparam>
    public interface IHandle<in TMessage>
    {
        /// <summary>
        ///     Handles the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        void Handle(TMessage message);
    }

    /// <summary>
    ///     Class TypedActor.
    /// </summary>
    [Obsolete("This class is renamed to HandleActor",false)]
    public abstract class TypedActor : ActorBase
    {
        /// <summary>
        ///     Processor for user defined messages.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override sealed bool Receive(object message)
        {
            MethodInfo method = GetType().GetMethod("Handle", new[] {message.GetType()});
            if (method == null)
            {
                return false;
            }

            method.Invoke(this, new[] {message});
            return true;
        }
    }
}