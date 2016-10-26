//-----------------------------------------------------------------------
// <copyright file="TypedActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
    public abstract class TypedActor : ActorBase
    {
        /// <summary>
        ///     Processor for user defined messages.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override sealed bool Receive(object message)
        {
            //dynamic method resolution will resolve the method on runtime
            //it is static cached for each type to perform well
            Handle((dynamic)message); 
            return true;
        }
        
        //default handler
        protected virtual void Handle(Object message)
        {          
            //todo: Handle should return wasHandled as Receive
            Unhandled(message);
        }
    }
}

