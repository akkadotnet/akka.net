//-----------------------------------------------------------------------
// <copyright file="ExtendedActorSystem.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    /// <summary>
    /// More powerful interface to the actor system’s implementation which is presented to 
    /// extensions (see <see cref="IExtension"/>).
    /// <remarks>Important Notice:<para>
    /// This class is not meant to be extended by user code. If you want to
    /// actually roll your own Akka, beware that you are completely on your own in
    /// that case!</para></remarks>
    /// </summary>
    public abstract class ExtendedActorSystem : ActorSystem
    {
        /// <summary>Gets the provider.</summary>
        /// <value>The provider.</value>
        public abstract IActorRefProvider Provider { get; }

        /// <summary>
        /// Gets the top-level supervisor of all user actors created using 
        /// <see cref="ActorSystem.ActorOf">system.ActorOf(...)</see>
        /// </summary>
        public abstract IInternalActorRef Guardian { get; }

        /// <summary>
        /// The <see cref="RootGuardianActorRef"/>, used as the lookup for <see cref="IActorRef"/> resolutions.
        /// </summary>
        public abstract IInternalActorRef LookupRoot { get; }

        /// <summary>
        /// Gets the top-level supervisor of all system-internal services like logging.
        /// </summary>
        public abstract IInternalActorRef SystemGuardian { get; }

        /// <summary>
        /// Gets the actor producer pipeline resolver for current actor system. It may be used by
        /// Akka plugins to inject custom behavior directly into actor creation chain.
        /// </summary>
        [Obsolete("Actor producer pipeline API will be removed in v1.5.")]
        public abstract ActorProducerPipelineResolver ActorPipelineResolver { get; }

        /// <summary>
        /// Creates a new system actor in the "/system" namespace. This actor 
        /// will be shut down during system shutdown only after all user actors have
        /// terminated.
        /// </summary>
        /// <param name="props">The <see cref="Props"/> used to create the actor</param>
        /// <param name="name">The name of the actor to create. The default value is <see langword="null"/>.</param>
        /// <returns>A reference to the newly created actor</returns>
        public abstract IActorRef SystemActorOf(Props props, string name = null);

        /// <summary>
        /// Creates a new system actor in the "/system" namespace. This actor 
        /// will be shut down during system shutdown only after all user actors have
        /// terminated.
        /// </summary>
        /// <typeparam name="TActor">The type of actor to create</typeparam>
        /// <param name="name">The name of the actor to create. The default value is <see langword="null"/>.</param>
        /// <returns>A reference to the newly created actor</returns>
        public abstract IActorRef SystemActorOf<TActor>(string name = null) where TActor : ActorBase, new();

        /// <summary>
        /// Aggressively terminates an <see cref="ActorSystem"/> without waiting for the normal shutdown process to run as-is.
        /// </summary>
        public abstract void Abort();

        //TODO: Missing threadFactory, dynamicAccess, printTree
        //  /**
        //  * A ThreadFactory that can be used if the transport needs to create any Threads
        //  */
        //  def threadFactory: ThreadFactory

        //  /**
        //  * ClassLoader wrapper which is used for reflective accesses internally. This is set
        //  * to use the context class loader, if one is set, or the class loader which
        //  * loaded the ActorSystem implementation. The context class loader is also
        //  * set on all threads created by the ActorSystem, if one was set during
        //  * creation.
        //  */
        //  def dynamicAccess: DynamicAccess

        //  /**
        //  * For debugging: traverse actor hierarchy and make string representation.
        //  * Careful, this may OOM on large actor systems, and it is only meant for
        //  * helping debugging in case something already went terminally wrong.
        //  */
        //  private[akka] def printTree: String
    }
}
