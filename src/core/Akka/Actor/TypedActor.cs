//-----------------------------------------------------------------------
// <copyright file="TypedActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Util.Internal;
using System;
using System.Collections.Generic;
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
        private static readonly object ActorCacheCreateLocker = new object();

        private static Dictionary<Type, Dictionary<Type, MethodInfo>> ActorHandleCache { get; } = new Dictionary<Type, Dictionary<Type, MethodInfo>>();

        /// <summary>
        ///     Processor for user defined messages.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <returns>TBD</returns>
        protected sealed override bool Receive(object message)
        {
            Type[] msgTypeArray = new[] {message.GetType()};
            Type actorType = GetType();
            MethodInfo handleMethod;
            Dictionary<Type, MethodInfo> handleCache;

            //safely create HandleCache for each TypedActor type
            if (!ActorHandleCache.TryGetValue(actorType, out handleCache))
            {
                lock (ActorCacheCreateLocker)
                {
                    if (!ActorHandleCache.TryGetValue(actorType, out handleCache))
                    {
                        handleCache = new Dictionary<Type, MethodInfo>();
                        ActorHandleCache.AddOrSet(actorType, handleCache);
                    }
                }
            }

            //Try get MethodInfo from HandleCache
            if (!handleCache.TryGetValue(msgTypeArray[0], out handleMethod))
            {
                handleMethod = actorType.GetMethod("Handle", msgTypeArray);
                if (handleMethod == null)
                {
                    //Check whether actor implements Handle<T> explicitly
                    Type closedIHandleType = typeof(IHandle<>).MakeGenericType(msgTypeArray);
                    if (closedIHandleType.IsAssignableFrom(actorType))
                    {
                        handleMethod = closedIHandleType.GetMethod("Handle", msgTypeArray);
                    }
                    else
                    {
                        return false;
                    }
                }
                handleCache.AddOrSet(msgTypeArray[0], handleMethod);
            }

            handleMethod.Invoke(this, new[] {message});
            return true;
        }
    }
}

