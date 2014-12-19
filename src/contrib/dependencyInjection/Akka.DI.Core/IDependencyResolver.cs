using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DI.Core
{
    /// <summary>
    /// Contract used provide services to ActorSytem Extension system used to creat
    /// Actors
    /// </summary>
    public interface IDependencyResolver
    {
        /// <summary>
        /// Gets the Type for the Actor Type specified in the actorName
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
    }
}
