//-----------------------------------------------------------------------
// <copyright file="IDependencyResolver.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.DI.Core
{
    /// <summary>
    /// Contract used provide services to ActorSystem Extension system used to create
    /// Actors
    /// </summary>
    public interface IDependencyResolver
    {
        /// <summary>
        /// Returns the Type for the Actor Type specified in the actorName
        /// </summary>
        /// <param name="actorName"></param>
        /// <returns>Type of the Actor specified in the actorName</returns>
        Type GetType(string actorName);
        /// <summary>
        /// Creates a delegate factory based on the actorName
        /// </summary>
        /// <param name="actorName">Name of the ActorType</param>
        /// <returns>factory delegate</returns>
        Func<ActorBase> CreateActorFactory(string actorName);
        /// <summary>
        /// Used Register the Configuration for the ActorType specified in TActor
        /// </summary>
        /// <typeparam name="TActor"></typeparam>
        /// <returns>Props configuration instance</returns>
        Props Create<TActor>() where TActor : ActorBase;
        /// <summary>
        /// This method is used to signal the DI Container that it can
        /// release it's reference to the actor.  <see href="http://www.amazon.com/Dependency-Injection-NET-Mark-Seemann/dp/1935182501/ref=sr_1_1?ie=UTF8&qid=1425861096&sr=8-1&keywords=mark+seemann">HERE</see> 
        /// </summary>
        /// <param name="actor"></param>
        void Release(ActorBase actor);
    }
}
