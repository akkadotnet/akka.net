//-----------------------------------------------------------------------
// <copyright file="DIActorProducer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.DI.Core
{
    /// <summary>
    /// Dependency Injection Backed IndirectActorProducer
    /// </summary>
    public class DIActorProducer : IIndirectActorProducer
    {
        private IDependencyResolver dependencyResolver;
        private Type actorType;

        readonly Func<ActorBase> actorFactory;

        public DIActorProducer(IDependencyResolver dependencyResolver,
                               Type actorType)
        {
            if (dependencyResolver == null) throw new ArgumentNullException("dependencyResolver");
            if (actorType == null) throw new ArgumentNullException("actorType");

            this.dependencyResolver = dependencyResolver;
            this.actorType = actorType;
            this.actorFactory = dependencyResolver.CreateActorFactory(actorType);
        }
        /// <summary>
        /// The System.Type of the Actor specified in the constructor parameter actorName
        /// </summary>
        public Type ActorType
        {
            get { return this.actorType.GetType(); }
        }
        /// <summary>
        /// Creates an instance of the Actor based on the Type specified in the constructor parameter actorName
        /// </summary>
        /// <returns></returns>
        public ActorBase Produce()
        {
            return actorFactory();
        }

        /// <summary>
        /// This method is used to signal the DI Container that it can
        /// release it's reference to the actor.  <see href="http://www.amazon.com/Dependency-Injection-NET-Mark-Seemann/dp/1935182501/ref=sr_1_1?ie=UTF8&qid=1425861096&sr=8-1&keywords=mark+seemann">HERE</see> 
        /// </summary>
        /// <param name="actor"></param>

        public void Release(ActorBase actor)
        {
            dependencyResolver.Release(actor);
        }

        
    }
}

