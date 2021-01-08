//-----------------------------------------------------------------------
// <copyright file="Resolver.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using System;

namespace Akka.Util
{
    /// <summary>
    /// TBD
    /// </summary>
    public interface IResolver
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="args">TBD</param>
        /// <returns>TBD</returns>
        T Resolve<T>(object[] args);
    }

    /// <summary>
    /// TBD
    /// </summary>
    public abstract class Resolve : IIndirectActorProducer
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public abstract ActorBase Produce();
        /// <summary>
        /// TBD
        /// </summary>
        public abstract Type ActorType { get; }

        /// <summary>
        /// TBD
        /// </summary>
        protected static IResolver Resolver { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="resolver">TBD</param>
        public static void SetResolver(IResolver resolver)
        {
            Resolver = resolver;
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public void Release(ActorBase actor)
        {
            actor = null;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class Resolve<TActor> : Resolve where TActor : ActorBase
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="args">TBD</param>
        public Resolve(params object[] args)
        {
            Arguments = args;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// This exception is thrown if the current <see cref="Resolve.Resolver"/> is undefined.
        /// </exception>
        /// <returns>TBD</returns>
        public override ActorBase Produce()
        {
            if (Resolver == null)
            {
                throw new InvalidOperationException("Resolver is not initialized");
            }
            return Resolver.Resolve<TActor>(Arguments);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override Type ActorType { get { return typeof(TActor); } }
        /// <summary>
        /// TBD
        /// </summary>
        public object[] Arguments { get; private set; }
    }
}
