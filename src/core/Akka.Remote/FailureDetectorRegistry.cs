//-----------------------------------------------------------------------
// <copyright file="FailureDetectorRegistry.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Annotations;
using Akka.Configuration;
using Akka.Event;

namespace Akka.Remote
{
    /// <summary>
    /// Interface for a registry of Akka <see cref="FailureDetector"/>s. New resources are implicitly registered when heartbeat is first
    /// called with the resource given as parameter.
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public interface IFailureDetectorRegistry<in T>
    {
        /// <summary>
        /// Returns true if the resource is considered to be up and healthy, false otherwise.
        /// For unregistered resources it returns true.
        /// </summary>
        /// <param name="resource">TBD</param>
        /// <returns>TBD</returns>
        bool IsAvailable(T resource);

        /// <summary>
        /// Returns true if the failure detector has received any heartbeats and started monitoring
        /// the resource.
        /// </summary>
        /// <param name="resource">TBD</param>
        /// <returns>TBD</returns>
        bool IsMonitoring(T resource);

        /// <summary>
        /// Records a heartbeat for a resource. If the resource is not yet registered (i.e. this is the first heartbeat) then
        /// is it automatically registered.
        /// </summary>
        /// <param name="resource">TBD</param>
        void Heartbeat(T resource);

        /// <summary>
        /// Remove the heartbeat management for a resource
        /// </summary>
        /// <param name="resource">TBD</param>
        void Remove(T resource);

        /// <summary>
        /// Removes all resources and any associated failure detector state.
        /// </summary>
        void Reset();
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Utility class to create <see cref="FailureDetector"/> instances via reflection.
    /// </summary>
    [InternalApi]
    public static class FailureDetectorLoader
    {
        /// <summary>
        /// Loads an instantiates a given <see cref="FailureDetector"/> implementation. The class to be loaded must have a constructor
        /// that accepts a <see cref="Config"/> and an <see cref="EventStream"/> parameter. Will throw <see cref="ConfigurationException"/>
        /// if the implementation cannot be loaded.
        /// </summary>
        /// <param name="fqcn">The fully-qualified .NET assembly name of the FailureDetector implementation class to be loaded.</param>
        /// <param name="config">Configuration that will be passed to the implementation.</param>
        /// <param name="system">ActorSystem to be used for loading the implementation.</param>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown when the given <paramref name="fqcn"/> could not be resolved.
        /// </exception>
        /// <returns>A configured instance of the given <see cref="FailureDetector"/> implementation.</returns>
        public static FailureDetector Load(string fqcn, Config config, ActorSystem system)
        {
            var failureDetectorClass = Type.GetType(fqcn);
            if (failureDetectorClass == null)
            {
                throw new ConfigurationException($"Could not create custom FailureDetector {fqcn}");
            }
            var failureDetector = (FailureDetector) Activator.CreateInstance(failureDetectorClass, config, system.EventStream);
            return failureDetector;
        }

        /// <summary>
        /// Loads an instantiates a given <see cref="FailureDetector"/> implementation. The class to be loaded must have a constructor
        /// that accepts a <see cref="Config"/> and an <see cref="EventStream"/> parameter. Will throw <see cref="ConfigurationException"/>
        /// if the implementation cannot be loaded.
        /// </summary>
        /// <param name="context">The ActorContext used to resolve an <see cref="ActorSystem"/> for this <see cref="FailureDetector"/> instance.</param>
        /// <param name="fqcn">The fully-qualified .NET assembly name of the FailureDetector implementation class to be loaded.</param>
        /// <param name="config">Configuration that will be passed to the implementation.</param>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown when the given <paramref name="fqcn"/> could not be resolved.
        /// </exception>
        /// <returns>A configured instance of the given <see cref="FailureDetector"/> implementation.</returns>
        public static FailureDetector LoadFailureDetector(this IActorContext context, string fqcn, Config config)
        {
            return Load(fqcn, config, context.System);
        }
    }
}
