using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Cluster
{
    /// <summary>
    /// API for plugins that will handle downing of cluster nodes. Concrete plugins must subclass and
    /// have a public one argument constructor accepting an [[akka.actor.ActorSystem]].
    /// </summary>
    public interface IDowningProvider
    {
        /// <summary>
        /// Time margin after which shards or singletons that belonged to a downed/removed
        /// partition are created in surviving partition. The purpose of this margin is that
        /// in case of a network partition the persistent actors in the non-surviving partitions
        /// must be stopped before corresponding persistent actors are started somewhere else.
        /// This is useful if you implement downing strategies that handle network partitions,
        /// e.g. by keeping the larger side of the partition and shutting down the smaller side.
        /// </summary>
        TimeSpan DownRemovalMargin { get; }

        /// <summary>
        /// If a props is returned it is created as a child of the core cluster daemon on cluster startup.
        /// It should then handle downing using the regular <see cref="Cluster"/> APIs.
        /// The actor will run on the same dispatcher as the cluster actor if dispatcher not configured.
        /// 
        /// May throw an exception which will then immediately lead to Cluster stopping, as the downing
        /// provider is vital to a working cluster.
        /// </summary>
        Props DowningActorProps { get; }
    }

    /// <summary>
    /// Default downing provider used when no provider is configured and 'auto-down-unreachable-after'
    /// is not enabled.
    /// </summary>
    public sealed class NoDowning : IDowningProvider
    {
        private readonly ActorSystem _system;

        public NoDowning(ActorSystem system)
        {
            _system = system;
        }

        public TimeSpan DownRemovalMargin => Cluster.Get(_system).Settings.DownRemovalMargin;
        public Props DowningActorProps => null;
    }

    internal static class DowningProvider
    {
        public static IDowningProvider Load(Type downingProviderType, ActorSystem system)
        {
            var extendedSystem = system as ExtendedActorSystem;
            try
            {
                return (IDowningProvider)Activator.CreateInstance(downingProviderType, extendedSystem);
            }
            catch (Exception e)
            {
                throw new ConfigurationException($"Couldn't create downing provider of type [{downingProviderType.FullName}]", e);
            }
        }
    }

}