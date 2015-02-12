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
        public abstract ActorRefProvider Provider { get; }

        /// <summary>
        /// Gets the top-level supervisor of all user actors created using 
        /// <see cref="ActorSystem.ActorOf">system.ActorOf(...)</see>
        /// </summary>
        public abstract InternalActorRef Guardian { get; }


        /// <summary>
        /// Gets the top-level supervisor of all system-internal services like logging.
        /// </summary>
        public abstract InternalActorRef SystemGuardian { get; }

        /// <summary>
        /// Gets the actor producer pipeline resolver for current actor system. It may be used by
        /// Akka plugins to inject custom behavior directly into actor creation chain.
        /// </summary>
        public abstract ActorProducerPipelineResolver ActorPipelineResolver { get; }

        /// <summary>Creates a new system actor in the "/system" namespace. This actor 
        /// will be shut down during system shutdown only after all user actors have
        /// terminated.</summary>
        public abstract ActorRef SystemActorOf(Props props, string name = null);

        /// <summary>Creates a new system actor in the "/system" namespace. This actor 
        /// will be shut down during system shutdown only after all user actors have
        /// terminated.</summary>
        public abstract ActorRef SystemActorOf<TActor>(string name = null) where TActor : ActorBase, new();

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