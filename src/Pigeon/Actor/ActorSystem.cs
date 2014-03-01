using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using System.Collections.Concurrent;
using Akka.Dispatch;
using Akka.Configuration;
using Akka.Event;

namespace Akka.Actor
{
    /// <summary>
    /// Class ActorSystemExtension.
    /// </summary>
    public abstract class ActorSystemExtension
    {
        /// <summary>
        /// Starts the specified system.
        /// </summary>
        /// <param name="system">The system.</param>
        public abstract void Start(ActorSystem system);
    }

    /// <summary>
    /// An actor system is a hierarchical group of actors which share common
    /// configuration, e.g. dispatchers, deployments, remote capabilities and
    /// addresses. It is also the entry point for creating or looking up actors.
    /// There are several possibilities for creating actors (see [[Akka.Actor.Props]]
    /// for details on `props`):
    /// <code>
    // C#
    /// system.ActorOf(props, "name");
    /// system.ActorOf(props);
    /// system.ActorOf(Props.Create(typeof(MyActor)), "name");
    /// system.ActorOf(Props.Create(() =&gt; new MyActor(arg1, arg2), "name");
    /// </code>
    /// Where no name is given explicitly, one will be automatically generated.
    /// <b><i>Important Notice:</i></b>
    /// This class is not meant to be extended by user code.
    /// </summary>
    public class ActorSystem : IActorRefFactory , IDisposable
    {

        /// <summary>
        /// Gets the provider.
        /// </summary>
        /// <value>The provider.</value>
        public ActorRefProvider Provider { get; private set; }

        /// <summary>
        /// Creates a new ActorSystem with the specified name, and the specified Config
        /// </summary>
        /// <param name="name">Name of the ActorSystem</param>
        /// <param name="config">Configuration of the ActorSystem</param>
        /// <param name="extensions">Extensions of the ActorSystem</param>
        /// <returns>ActorSystem.</returns>
        public static ActorSystem Create(string name, Config config, params ActorSystemExtension[] extensions)
        {
            return new ActorSystem(name, config, extensions);
        }


        /// <summary>
        /// Creates the specified name.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="extensions">The extensions.</param>
        /// <returns>ActorSystem.</returns>
        public static ActorSystem Create(string name, params ActorSystemExtension[] extensions)
        {
            return new ActorSystem(name, null, extensions);
        }

        /// <summary>
        /// Creates the specified name.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>ActorSystem.</returns>
        public static ActorSystem Create(string name)
        {
            return new ActorSystem(name, null);
        }

        /// <summary>
        /// The extensions
        /// </summary>
        private List<ActorSystemExtension> extensions = new List<ActorSystemExtension>();

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorSystem"/> class.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="config">The configuration.</param>
        /// <param name="extensions">The extensions.</param>
        public ActorSystem(string name,Config config=null,params ActorSystemExtension[] extensions)
        {
            this.Name = name;
            ConfigureScheduler();
            ConfigureSettings(config);
            ConfigureDeployer();
            ConfigureEventStream();
            ConfigureSerialization();
            ConfigureMailboxes();
            ConfigureDispatchers();
            ConfigureProvider();
            ConfigureExtensions(extensions);
            this.Start();
        }

        /// <summary>
        /// Configures the scheduler.
        /// </summary>
        private void ConfigureScheduler()
        {
            this.Scheduler = new Scheduler();
        }

        /// <summary>
        /// Configures the deployer.
        /// </summary>
        private void ConfigureDeployer()
        {
            this.Deployer = new Deployer(this.Settings);
        }

        /// <summary>
        /// Configures the extensions.
        /// </summary>
        /// <param name="extensions">The extensions.</param>
        private void ConfigureExtensions(ActorSystemExtension[] extensions)
        {
            if (extensions != null)
            {
                this.extensions.AddRange(extensions);
                this.extensions.ForEach(e => e.Start(this));
            }
        }

        /// <summary>
        /// Configures the settings.
        /// </summary>
        /// <param name="config">The configuration.</param>
        private void ConfigureSettings(Config config)
        {
            this.Settings = new Settings(this, config);
        }

        /// <summary>
        /// Configures the event stream.
        /// </summary>
        private void ConfigureEventStream()
        {
            this.EventStream = new EventStream(Settings.DebugEventStream);
            this.EventStream.StartStdoutLogger(Settings);
        }

        /// <summary>
        /// Configures the serialization.
        /// </summary>
        private void ConfigureSerialization()
        {
            this.Serialization = new Serialization.Serialization(this);
        }

        /// <summary>
        /// Configures the mailboxes.
        /// </summary>
        private void ConfigureMailboxes()
        {
            this.Mailboxes = new Mailboxes(this);
        }

        /// <summary>
        /// Configures the provider.
        /// </summary>
        private void ConfigureProvider()
        {
            var providerType = Type.GetType(Settings.ProviderClass);
            var provider = (ActorRefProvider)Activator.CreateInstance(providerType, this);
            this.Provider = provider;
            this.Provider.Init();
        }

        /// <summary>
        /// Starts this instance.
        /// </summary>
        private void Start()
        {
            if (Settings.LogDeadLetters > 0)
                this.logDeadLetterListener = this.SystemActorOf<DeadLetterListener>("deadLetterListener");

            EventStream.StartDefaultLoggers(this);

            this.log = new BusLogging(EventStream, "ActorSystem(" + Name + ")", this.GetType());

            if (Settings.LogConfigOnStart)
            {
                log.Warn(this.Settings.ToString());
            }
        }

        /// <summary>
        /// Configures the dispatchers.
        /// </summary>
        private void ConfigureDispatchers()
        {
            this.Dispatchers = new Dispatchers(this);           
        }

        /// <summary>
        /// Gets the settings.
        /// </summary>
        /// <value>The settings.</value>
        public Settings Settings { get;private set; }
        /// <summary>
        /// Gets the name.
        /// </summary>
        /// <value>The name.</value>
        public string Name { get;private set; }

        /// <summary>
        /// Gets the serialization.
        /// </summary>
        /// <value>The serialization.</value>
        public Serialization.Serialization Serialization { get;private set; }

        /// <summary>
        /// The log
        /// </summary>
        public LoggingAdapter log;
        /// <summary>
        /// The log dead letter listener
        /// </summary>
        private InternalActorRef logDeadLetterListener;

        /// <summary>
        /// Stop this actor system. This will stop the guardian actor, which in turn
        /// will recursively stop all its child actors, then the system guardian
        /// (below which the logging actors reside) and the execute all registered
        /// termination handlers (<see cref="ActorSystem.RegisterOnTermination" />).
        /// </summary>
        public void Shutdown()
        {
            Provider.RootCell.Stop();
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            this.Shutdown();
        }

        /// <summary>
        /// Systems the actor of.
        /// </summary>
        /// <param name="props">The props.</param>
        /// <param name="name">The name.</param>
        /// <returns>InternalActorRef.</returns>
        public InternalActorRef SystemActorOf(Props props, string name = null)
        {
            return Provider.SystemGuardian.Cell.ActorOf(props, name);            
        }

        /// <summary>
        /// Systems the actor of.
        /// </summary>
        /// <typeparam name="TActor">The type of the t actor.</typeparam>
        /// <param name="name">The name.</param>
        /// <returns>InternalActorRef.</returns>
        public InternalActorRef SystemActorOf<TActor>(string name = null) where TActor : ActorBase
        {
            return Provider.SystemGuardian.Cell.ActorOf<TActor>(name);
        }

        /// <summary>
        /// Create new actor as child of this context with the given name, which must
        /// not start with “$”. If the given name is already in use,
        /// and `InvalidActorNameException` is thrown.
        /// See [[Akka.Actor.Props]] for details on how to obtain a `Props` object.
        /// @throws akka.actor.InvalidActorNameException if the given name is
        /// invalid or already in use
        /// @throws akka.ConfigurationException if deployment, dispatcher
        /// or mailbox configuration is wrong
        /// </summary>
        /// <param name="props">The props.</param>
        /// <param name="name">The name.</param>
        /// <returns>InternalActorRef.</returns>
        public InternalActorRef ActorOf(Props props, string name = null)
        {
            return Provider.Guardian.Cell.ActorOf(props, name);
        }

        /// <summary>
        /// Actors the of.
        /// </summary>
        /// <typeparam name="TActor">The type of the t actor.</typeparam>
        /// <param name="name">The name.</param>
        /// <returns>InternalActorRef.</returns>
        public InternalActorRef ActorOf<TActor>(string name = null) where TActor : ActorBase
        {
            return Provider.Guardian.Cell.ActorOf<TActor>( name);
        }

        /// <summary>
        /// Construct an [[Akka.Actor.ActorSelection]] from the given path, which is
        /// parsed for wildcards (these are replaced by regular expressions
        /// internally). No attempt is made to verify the existence of any part of
        /// the supplied path, it is recommended to send a message and gather the
        /// replies in order to resolve the matching set of actors.
        /// </summary>
        /// <param name="actorPath">The actor path.</param>
        /// <returns>ActorSelection.</returns>
        public ActorSelection ActorSelection(ActorPath actorPath)
        {
            return Provider.RootCell.ActorSelection(actorPath);
        }

        /// <summary>
        /// Construct an [[Akka.Actor.ActorSelection]] from the given path, which is
        /// parsed for wildcards (these are replaced by regular expressions
        /// internally). No attempt is made to verify the existence of any part of
        /// the supplied path, it is recommended to send a message and gather the
        /// replies in order to resolve the matching set of actors.
        /// </summary>
        /// <param name="actorPath">The actor path.</param>
        /// <returns>ActorSelection.</returns>
        public ActorSelection ActorSelection(string actorPath)
        {
            return Provider.RootCell.ActorSelection(actorPath);
        }



        /// <summary>
        /// Gets the event stream.
        /// </summary>
        /// <value>The event stream.</value>
        public EventStream EventStream { get; private set; }

        /// <summary>
        /// Gets the dead letters.
        /// </summary>
        /// <value>The dead letters.</value>
        public ActorRef DeadLetters
        {
            get
            {
                return Provider.DeadLetters;
            }
        }

        /// <summary>
        /// Gets the guardian.
        /// </summary>
        /// <value>The guardian.</value>
        public InternalActorRef Guardian
        {
            get
            {
                return this.Provider.Guardian ;
            }
        }

        /// <summary>
        /// Gets the system guardian.
        /// </summary>
        /// <value>The system guardian.</value>
        public InternalActorRef SystemGuardian
        {
            get
            {
                return this.Provider.SystemGuardian;
            }
        }

        /// <summary>
        /// Gets the dispatchers.
        /// </summary>
        /// <value>The dispatchers.</value>
        public Dispatchers Dispatchers { get;private set; }
        /// <summary>
        /// Gets the mailboxes.
        /// </summary>
        /// <value>The mailboxes.</value>
        public Mailboxes Mailboxes { get;private set; }
        /// <summary>
        /// Gets the deployer.
        /// </summary>
        /// <value>The deployer.</value>
        public Deployer Deployer { get;private set; }
        /// <summary>
        /// Gets the scheduler.
        /// </summary>
        /// <value>The scheduler.</value>
        public Scheduler Scheduler { get;private set; }
    }
}