//-----------------------------------------------------------------------
// <copyright file="ActorSystem.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor.Internal;
using Akka.Actor.Setup;
using Akka.Dispatch;
using Akka.Event;
using Akka.Util;
using ConfigurationFactory = Akka.Configuration.ConfigurationFactory;
using Config = Akka.Configuration.Config;

namespace Akka.Actor
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    public abstract class ProviderSelection
    {
        private ProviderSelection(string identifier, string fqn, bool hasCluster)
        {
            Identifier = identifier;
            Fqn = fqn;
            HasCluster = hasCluster;
        }

        internal string Identifier { get; }
        internal string Fqn { get; }
        internal bool HasCluster { get; }

        internal const string LocalActorRefProvider = "Akka.Actor.LocalActorRefProvider, Akka";
        internal const string RemoteActorRefProvider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote";
        internal const string ClusterActorRefProvider = "Akka.Cluster.ClusterActorRefProvider, Akka.Cluster";

        public sealed class Local : ProviderSelection
        {
            private Local() : base("local", LocalActorRefProvider, false)
            {
            }

            public static readonly Local Instance = new Local();
        }

        public sealed class Remote : ProviderSelection
        {
            private Remote() : base("remote", RemoteActorRefProvider, false)
            {
            }

            public static readonly Remote Instance = new Remote();
        }

        public sealed class Cluster : ProviderSelection
        {
            private Cluster() : base("cluster", ClusterActorRefProvider, true)
            {
            }

            public static readonly Cluster Instance = new Cluster();
        }

        public sealed class Custom : ProviderSelection
        {
            public Custom(string fqn, string identifier = null, bool hasCluster = false) : base(
                identifier ?? string.Empty, fqn, hasCluster)
            {
            }
        }

        internal static ProviderSelection GetProvider(string providerClass)
        {
            switch (providerClass)
            {
                case "local":
                    return Local.Instance;
                case "remote":
                case RemoteActorRefProvider: // additional case for older configurations
                    return Remote.Instance;
                case "cluster":
                case ClusterActorRefProvider: // additional case for older configurations
                    return Cluster.Instance;
                default:
                    return new Custom(providerClass);
            }
        }
    }

    /// <summary>
    /// Core boostrap settings for the <see cref="ActorSystem"/>, which can be created using one of the static factory methods
    /// on this class.
    /// </summary>
    public sealed class BootstrapSetup : Setup.Setup
    {
        internal BootstrapSetup() : this(Option<Config>.None, Option<ProviderSelection>.None)
        {
        }

        internal BootstrapSetup(Option<Config> config, Option<ProviderSelection> actorRefProvider)
        {
            Config = config;
            ActorRefProvider = actorRefProvider;
        }

        /// <summary>
        /// Configuration to use for the <see cref="ActorSystem"/>. If no <see cref="Config"/> is given, the default reference
        /// configuration will be loaded via <see cref="ConfigurationFactory.Load"/>.
        /// </summary>
        public Option<Config> Config { get; }

        /// <summary>
        /// Overrides the 'akka.actor.provider' setting in config. Can be 'local' (default), 'remote', or cluster.
        ///
        /// It can also be the fully qualified class name of an ActorRefProvider.
        /// </summary>
        public Option<ProviderSelection> ActorRefProvider { get; }

        /// <summary>
        /// Create a new <see cref="BootstrapSetup"/> instance.
        /// </summary>
        public static BootstrapSetup Create()
        {
            return new BootstrapSetup();
        }

        public BootstrapSetup WithActorRefProvider(ProviderSelection name)
        {
            return new BootstrapSetup(Config, name);
        }

        public BootstrapSetup WithConfig(Config config)
        {
            return new BootstrapSetup(config, ActorRefProvider);
        }
    }

    /// <summary>
    ///     An actor system is a hierarchical group of actors which share common
    ///     configuration, e.g. dispatchers, deployments, remote capabilities and
    ///     addresses. It is also the entry point for creating or looking up actors.
    ///     There are several possibilities for creating actors (see <see cref="Akka.Actor.Props"/>
    ///     for details on `props`):
    ///     <code>
    /// system.ActorOf(props, "name");
    /// system.ActorOf(props);
    /// system.ActorOf(Props.Create(typeof(MyActor)), "name");
    /// system.ActorOf(Props.Create(() =&gt; new MyActor(arg1, arg2), "name");
    /// </code>
    ///     Where no name is given explicitly, one will be automatically generated.
    ///     <b>
    ///         <i>Important Notice:</i>
    ///     </b>
    ///     This class is not meant to be extended by user code.
    /// </summary>
    public abstract class ActorSystem : IActorRefFactory, IDisposable
    {
        /// <summary>Gets the settings.</summary>
        /// <value>The settings.</value>
        public abstract Settings Settings { get; }

        /// <summary>Gets the name of this system.</summary>
        /// <value>The name.</value>
        public abstract string Name { get; }

        /// <summary>Gets the serialization.</summary>
        /// <value>The serialization.</value>
        public abstract Serialization.Serialization Serialization { get; }

        /// <summary>Gets the event stream.</summary>
        /// <value>The event stream.</value>
        public abstract EventStream EventStream { get; }

        /// <summary>
        ///     Gets the dead letters.
        /// </summary>
        /// <value>The dead letters.</value>
        public abstract IActorRef DeadLetters { get; }

        /// <summary>Gets the dispatchers.</summary>
        /// <value>The dispatchers.</value>
        public abstract Dispatchers Dispatchers { get; }

        /// <summary>Gets the mailboxes.</summary>
        /// <value>The mailboxes.</value>
        public abstract Mailboxes Mailboxes { get; }


        /// <summary>Gets the scheduler.</summary>
        /// <value>The scheduler.</value>
        public abstract IScheduler Scheduler { get; }

        /// <summary>Gets the log</summary>
        public abstract ILoggingAdapter Log { get; }

        /// <summary>
        /// Start-up time since the epoch.
        /// </summary>
        public TimeSpan StartTime { get; } = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Up-time of this actor system.
        /// </summary>
        public TimeSpan Uptime => MonotonicClock.ElapsedHighRes - _creationTime;

        private readonly TimeSpan _creationTime = MonotonicClock.ElapsedHighRes;

        /// <summary>
        /// Creates a new <see cref="ActorSystem"/> with the specified name and configuration.
        /// </summary>
        /// <param name="name">The name of the actor system to create. The name must be uri friendly.
        /// <remarks>Must contain only word characters (i.e. [a-zA-Z0-9] plus non-leading '-'</remarks>
        /// </param>
        /// <param name="config">The configuration used to create the actor system</param>
        /// <returns>A newly created actor system with the given name and configuration.</returns>
        public static ActorSystem Create(string name, Config config)
        {
            return CreateAndStartSystem(name, config, ActorSystemSetup.Empty);
        }

        /// <summary>
        /// Shortcut for creating a new actor system with the specified name and settings.
        /// </summary>
        /// <param name="name">The name of the actor system to create. The name must be uri friendly.
        /// <remarks>Must contain only word characters (i.e. [a-zA-Z0-9] plus non-leading '-'</remarks>
        /// </param>
        /// <param name="setup">The bootstrap setup used to help programmatically initialize the <see cref="ActorSystem"/>.</param>
        /// <returns>A newly created actor system with the given name and configuration.</returns>
        public static ActorSystem Create(string name, BootstrapSetup setup)
        {
            return Create(name, ActorSystemSetup.Create(setup));
        }

        /// <summary>
        /// Shortcut for creating a new actor system with the specified name and settings.
        /// </summary>
        /// <param name="name">The name of the actor system to create. The name must be uri friendly.
        /// <remarks>Must contain only word characters (i.e. [a-zA-Z0-9] plus non-leading '-'</remarks>
        /// </param>
        /// <param name="setup">The bootstrap setup used to help programmatically initialize the <see cref="ActorSystem"/>.</param>
        /// <returns>A newly created actor system with the given name and configuration.</returns>
        public static ActorSystem Create(string name, ActorSystemSetup setup)
        {
            var bootstrapSetup = setup.Get<BootstrapSetup>();
            var appConfig = bootstrapSetup.FlatSelect(_ => _.Config).GetOrElse(ConfigurationFactory.Load());

            return CreateAndStartSystem(name, appConfig, setup);
        }

        /// <summary>
        /// Creates a new <see cref="ActorSystem"/> with the specified name.
        /// </summary>
        /// <param name="name">The name of the actor system to create. The name must be uri friendly.
        /// <remarks>Must contain only word characters (i.e. [a-zA-Z0-9] plus non-leading '-'</remarks>
        /// </param>
        /// <returns>A newly created actor system with the given name.</returns>
        public static ActorSystem Create(string name)
        {
            return Create(name, ActorSystemSetup.Empty);
        }

        private static ActorSystem CreateAndStartSystem(string name, Config withFallback, ActorSystemSetup setup)
        {
            var system = new ActorSystemImpl(name, withFallback, setup);
            system.Start();
            return system;
        }

        /// <summary>
        /// Retrieves the specified extension that is registered to this actor system.
        /// </summary>
        /// <param name="extensionId">The extension to retrieve</param>
        /// <returns>The specified extension registered to this actor system</returns>
        public abstract object GetExtension(IExtensionId extensionId);

        /// <summary>
        /// Retrieves an extension with the specified type that is registered to this actor system.
        /// </summary>
        /// <typeparam name="T">The type of extension to retrieve</typeparam>
        /// <returns>The specified extension registered to this actor system</returns>
        public abstract T GetExtension<T>() where T : class, IExtension;

        /// <summary>
        /// Determines whether this actor system has an extension with the specified type.
        /// </summary>
        /// <param name="type">The type of the extension being queried</param>
        /// <returns><c>true</c> if this actor system has the extension; otherwise <c>false</c>.</returns>
        public abstract bool HasExtension(Type type);

        /// <summary>
        /// Determines whether this actor system has the specified extension.
        /// </summary>
        /// <typeparam name="T">The type of the extension being queried</typeparam>
        /// <returns><c>true</c> if this actor system has the extension; otherwise <c>false</c>.</returns>
        public abstract bool HasExtension<T>() where T : class, IExtension;

        /// <summary>
        /// Tries to retrieve an extension with the specified type.
        /// </summary>
        /// <param name="extensionType">The type of extension to retrieve</param>
        /// <param name="extension">The extension that is retrieved if successful</param>
        /// <returns><c>true</c> if the retrieval was successful; otherwise <c>false</c>.</returns>
        public abstract bool TryGetExtension(Type extensionType, out object extension);

        /// <summary>
        /// Tries to retrieve an extension with the specified type
        /// </summary>
        /// <typeparam name="T">The type of extension to retrieve</typeparam>
        /// <param name="extension">The extension that is retrieved if successful</param>
        /// <returns><c>true</c> if the retrieval was successful; otherwise <c>false</c>.</returns>
        public abstract bool TryGetExtension<T>(out T extension) where T : class, IExtension;

        /// <summary>
        /// <para>
        /// Registers a block of code (callback) to run after ActorSystem.shutdown has been issued and all actors
        /// in this actor system have been stopped. Multiple code blocks may be registered by calling this method
        /// multiple times.
        /// </para>
        /// <para>
        /// The callbacks will be run sequentially in reverse order of registration, i.e. last registration is run first.
        /// </para>
        /// </summary>
        /// <param name="code">The code to run</param>
        /// <exception cref="Exception">
        /// This exception is thrown if the system has already shut down or if shutdown has been initiated.
        /// </exception>
        public abstract void RegisterOnTermination(Action code);

        /// <summary>
        /// <para>
        /// If `akka.coordinated-shutdown.run-by-actor-system-terminate` is configured to `off`
        /// it will not run `CoordinatedShutdown`, but the `ActorSystem` and its actors
        /// will still be terminated.        
        /// </para>
        /// <para>
        /// Terminates this actor system. This will stop the guardian actor, which in turn will recursively stop
        /// all its child actors, then the system guardian (below which the logging actors reside) and the execute
        /// all registered termination handlers (<see cref="ActorSystem.RegisterOnTermination" />).
        /// </para>
        /// <para>
        /// Be careful to not schedule any operations on completion of the returned task using the `dispatcher`
        /// of this actor system as it will have been shut down before the task completes.
        /// </para>
        /// </summary>
        /// <returns>
        /// A <see cref="Task"/> that will complete once the actor system has finished terminating and all actors are stopped.
        /// </returns>
        public abstract Task Terminate();

        internal abstract void FinalTerminate();

        /// <summary>
        /// Returns a task which will be completed after the <see cref="ActorSystem"/> has been
        /// terminated and termination hooks have been executed. Be careful to not schedule any
        /// operations on the `dispatcher` of this actor system as it will have been shut down
        /// before this task completes.
        /// </summary>
        public abstract Task WhenTerminated { get; }

        /// <summary>
        /// Stops the specified actor permanently.
        /// </summary>
        /// <param name="actor">The actor to stop</param>
        /// <remarks>
        /// This method has no effect if the actor is already stopped.
        /// </remarks>
        public abstract void Stop(IActorRef actor);

        private bool _isDisposed; //Automatically initialized to false;

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            //Take this object off the finalization queue and prevent finalization code for this object
            //from executing a second time.
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            // If disposing equals false, the method has been called by the
            // runtime from inside the finalizer and you should not reference
            // other objects. Only unmanaged resources can be disposed.

            try
            {
                //Make sure Dispose does not get called more than once, by checking the disposed field
                if (!_isDisposed)
                {
                    if (disposing)
                    {
                        Log.Debug("Disposing system");
                        Terminate().Wait(); // System needs to be disposed before method returns
                    }

                    //Clean up unmanaged resources
                }

                _isDisposed = true;
            }
            finally
            {

            }
        }

        /// <summary>
        /// Registers the specified extension with this actor system.
        /// </summary>
        /// <param name="extension">The extension to register with this actor system</param>
        /// <returns>The extension registered with this actor system</returns>
        public abstract object RegisterExtension(IExtensionId extension);

        /// <inheritdoc cref="IActorRefFactory"/>
        public abstract IActorRef ActorOf(Props props, string name = null);

        /// <inheritdoc cref="IActorRefFactory"/>
        public abstract ActorSelection ActorSelection(ActorPath actorPath);

        /// <inheritdoc cref="IActorRefFactory"/>
        public abstract ActorSelection ActorSelection(string actorPath);
    }
}