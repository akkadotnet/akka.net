//-----------------------------------------------------------------------
// <copyright file="ExtendedActorSystem.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
        /// Gets the top-level supervisor of all system-internal services like logging.
        /// </summary>
        public abstract IInternalActorRef SystemGuardian { get; }

        /// <summary>
        /// Gets the actor producer pipeline resolver for current actor system. It may be used by
        /// Akka plugins to inject custom behavior directly into actor creation chain.
        /// </summary>
        public abstract ActorProducerPipelineResolver ActorPipelineResolver { get; }

        /// <summary>Creates a new system actor in the "/system" namespace. This actor 
        /// will be shut down during system shutdown only after all user actors have
        /// terminated.</summary>
        public abstract IActorRef SystemActorOf(Props props, string name = null);

        /// <summary>Creates a new system actor in the "/system" namespace. This actor 
        /// will be shut down during system shutdown only after all user actors have
        /// terminated.</summary>
        public abstract IActorRef SystemActorOf<TActor>(string name = null) where TActor : ActorBase, new();

        /// <summary>The wrapper which is used by the framework to perform any reflective
        /// operations in order to retrieve types at runtime. By default, uses the same
        /// assembly which the ActorSystem was loaded into.</summary>
        public abstract IDynamicAccess DynamicAccess { get; }

        //TODO: Missing threadFactory, printTree
        //  /**
        //  * A ThreadFactory that can be used if the transport needs to create any Threads
        //  */
        //  def threadFactory: ThreadFactory
  
        //  /**
        //  * For debugging: traverse actor hierarchy and make string representation.
        //  * Careful, this may OOM on large actor systems, and it is only meant for
        //  * helping debugging in case something already went terminally wrong.
        //  */
        //  private[akka] def printTree: String
    }
}

