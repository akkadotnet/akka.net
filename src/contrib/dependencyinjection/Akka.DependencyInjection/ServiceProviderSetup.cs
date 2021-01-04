using System;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Event;

namespace Akka.DependencyInjection
{
    /// <summary>
    /// Used to help bootstrap an <see cref="ActorSystem"/> with dependency injection (DI)
    /// support via a <see cref="IServiceProvider"/> reference.
    ///
    /// The <see cref="IServiceProvider"/> will be used to access previously registered services
    /// in the creation of actors and other pieces of infrastructure inside Akka.NET.
    ///
    /// The constructor is internal. Please use <see cref="Create"/> to create a new instance.
    /// </summary>
    public class ServiceProviderSetup : Setup
    {
        internal ServiceProviderSetup(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public IServiceProvider ServiceProvider { get; }

        public static ServiceProviderSetup Create(IServiceProvider provider)
        {
            if(provider == null)
                throw new ArgumentNullException(nameof(provider));

            return new ServiceProviderSetup(provider);
        }
    }
}
