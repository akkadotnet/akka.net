using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.Tools;
using Debug = System.Diagnostics.Debug;

namespace Akka.Actor
{
    // C#
    /// <summary>
    ///     An actor system is a hierarchical group of actors which share common
    ///     configuration, e.g. dispatchers, deployments, remote capabilities and
    ///     addresses. It is also the entry point for creating or looking up actors.
    ///     There are several possibilities for creating actors (see [[Akka.Actor.Props]]
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
    public class ActorSystem : IActorRefFactory, IDisposable
    {
        /// <summary>
        ///     The extensionsBase
        /// </summary>
        private readonly ConcurrentDictionary<Type, object> _extensions = new ConcurrentDictionary<Type, object>();

        /// <summary>
        ///     The log
        /// </summary>
        public LoggingAdapter log;

        /// <summary>
        ///     The log dead letter listener
        /// </summary>
        private InternalActorRef logDeadLetterListener;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ActorSystem" /> class.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="config">The configuration.</param>
        public ActorSystem(string name, Config config = null)
        {
            if (!Regex.Match(name, "^[a-zA-Z0-9][a-zA-Z0-9-]*$").Success)
                throw new ArgumentException(
                    "invalid ActorSystem name [" + name +
                    "], must contain only word characters (i.e. [a-zA-Z0-9] plus non-leading '-')");

            Name = name;
            ConfigureScheduler();
            ConfigureSettings(config);
            ConfigureEventStream();
            ConfigureSerialization();
            ConfigureMailboxes();
            ConfigureDispatchers();
            ConfigureProvider();
            ConfigureLoggers();
            LoadExtensions();
            Start();
        }

        /// <summary>
        ///     Gets the provider.
        /// </summary>
        /// <value>The provider.</value>
        public ActorRefProvider Provider { get; private set; }

        /// <summary>
        ///     Gets the settings.
        /// </summary>
        /// <value>The settings.</value>
        public Settings Settings { get; private set; }

        /// <summary>
        ///     Gets the name.
        /// </summary>
        /// <value>The name.</value>
        public string Name { get; private set; }

        /// <summary>
        ///     Gets the serialization.
        /// </summary>
        /// <value>The serialization.</value>
        public Serialization.Serialization Serialization { get; private set; }

        /// <summary>
        ///     Gets the event stream.
        /// </summary>
        /// <value>The event stream.</value>
        public EventStream EventStream { get; private set; }

        /// <summary>
        ///     Gets the dead letters.
        /// </summary>
        /// <value>The dead letters.</value>
        public ActorRef DeadLetters
        {
            get { return Provider.DeadLetters; }
        }

        /// <summary>
        ///     Gets the guardian.
        /// </summary>
        /// <value>The guardian.</value>
        public InternalActorRef Guardian
        {
            get { return Provider.Guardian; }
        }

        /// <summary>
        ///     Gets the system guardian.
        /// </summary>
        /// <value>The system guardian.</value>
        public InternalActorRef SystemGuardian
        {
            get { return Provider.SystemGuardian; }
        }

        /// <summary>
        ///     Gets the dispatchers.
        /// </summary>
        /// <value>The dispatchers.</value>
        public Dispatchers Dispatchers { get; private set; }

        /// <summary>
        ///     Gets the mailboxes.
        /// </summary>
        /// <value>The mailboxes.</value>
        public Mailboxes Mailboxes { get; private set; }


        /// <summary>
        ///     Gets the scheduler.
        /// </summary>
        /// <value>The scheduler.</value>
        public Scheduler Scheduler { get; private set; }

        /// <summary>
        ///     Create new actor as child of this context with the given name, which must
        ///     not start with “$”. If the given name is already in use,
        ///     and `InvalidActorNameException` is thrown.
        ///     See [[Akka.Actor.Props]] for details on how to obtain a `Props` object.
        ///     @throws akka.actor.InvalidActorNameException if the given name is
        ///     invalid or already in use
        ///     @throws akka.ConfigurationException if deployment, dispatcher
        ///     or mailbox configuration is wrong
        /// </summary>
        /// <param name="props">The props.</param>
        /// <param name="name">The name.</param>
        /// <returns>InternalActorRef.</returns>
        public InternalActorRef ActorOf(Props props, string name = null)
        {
            return Provider.Guardian.Cell.ActorOf(props, name);
        }

        /// <summary>
        ///     Actors the of.
        /// </summary>
        /// <typeparam name="TActor">The type of the t actor.</typeparam>
        /// <param name="name">The name.</param>
        /// <returns>InternalActorRef.</returns>
        public InternalActorRef ActorOf<TActor>(string name = null) where TActor : ActorBase
        {
            return Provider.Guardian.Cell.ActorOf<TActor>(name);
        }

        /// <summary>
        ///     Construct an [[Akka.Actor.ActorSelection]] from the given path, which is
        ///     parsed for wildcards (these are replaced by regular expressions
        ///     internally). No attempt is made to verify the existence of any part of
        ///     the supplied path, it is recommended to send a message and gather the
        ///     replies in order to resolve the matching set of actors.
        /// </summary>
        /// <param name="actorPath">The actor path.</param>
        /// <returns>ActorSelection.</returns>
        public ActorSelection ActorSelection(ActorPath actorPath)
        {
            return Provider.RootCell.ActorSelection(actorPath);
        }

        /// <summary>
        ///     Construct an [[Akka.Actor.ActorSelection]] from the given path, which is
        ///     parsed for wildcards (these are replaced by regular expressions
        ///     internally). No attempt is made to verify the existence of any part of
        ///     the supplied path, it is recommended to send a message and gather the
        ///     replies in order to resolve the matching set of actors.
        /// </summary>
        /// <param name="actorPath">The actor path.</param>
        /// <returns>ActorSelection.</returns>
        public ActorSelection ActorSelection(string actorPath)
        {
            return Provider.RootCell.ActorSelection(actorPath);
        }

        /// <summary>
        ///     Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Shutdown();
        }

        /// <summary>
        ///     Creates a new ActorSystem with the specified name, and the specified Config
        /// </summary>
        /// <param name="name">Name of the ActorSystem</param>
        /// <param name="config">Configuration of the ActorSystem</param>
        /// <returns>ActorSystem.</returns>
        public static ActorSystem Create(string name, Config config)
        {
            return new ActorSystem(name, config);
        }

        /// <summary>
        ///     Creates the specified name.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>ActorSystem.</returns>
        public static ActorSystem Create(string name)
        {
            return new ActorSystem(name);
        }

        /// <summary>
        ///     Configures the scheduler.
        /// </summary>
        private void ConfigureScheduler()
        {
            Scheduler = new Scheduler();
        }

        /// <summary>
        /// Load all of the extensions registered in the <see cref="ActorSystem.Settings"/>
        /// </summary>
        private void LoadExtensions()
        {
            var extensions = new List<IExtensionId>();
            foreach (var extensionFqn in Settings.Config.GetStringList("akka.extensions"))
            {
                var extensionType = Type.GetType(extensionFqn);
                if (extensionType == null || !typeof(IExtensionId).IsAssignableFrom(extensionType) || extensionType.IsAbstract || !extensionType.IsClass)
                {
                    log.Error("[{0}] is not an 'ExtensionId', skipping...", extensionFqn);
                    continue;
                }

                try
                {
                    var extension = (IExtensionId) Activator.CreateInstance(extensionType);
                    extensions.Add(extension);
                }
                catch (Exception ex)
                {
                    log.Error(ex, "While trying to load extension [{0}], skipping...", extensionFqn);
                }
                
            }

            ConfigureExtensions(extensions);
        }

        /// <summary>
        ///     Configures the extensionsBase.
        /// </summary>
        /// <param name="extensionIdProviders">The extensionsBase.</param>
        private void ConfigureExtensions(IEnumerable<IExtensionId> extensionIdProviders)
        {
            foreach (var extensionId in extensionIdProviders)
            {
                RegisterExtension(extensionId);
            }
        }

        /// <summary>
        /// Registers a new extensionBase to the ActorSystem
        /// </summary>
        /// <param name="extensionBase">The extensionBase to register</param>
        public object RegisterExtension(IExtensionId extensionBase)
        {
            if (extensionBase == null) return null;
            if (!_extensions.ContainsKey(extensionBase.ExtensionType))
            {
                _extensions.TryAdd(extensionBase.ExtensionType, extensionBase.CreateExtension(this));
            }

            return extensionBase.Get(this);
        }

        /// <summary>
        /// Returns an extension registered to this ActorSystem
        /// </summary>
        public object GetExtension(IExtensionId extensionId)
        {
            object extension;
            _extensions.TryGetValue(extensionId.ExtensionType, out extension);
            return extension;
        }

        public object GetExtension<T>()where T:IExtension
        {
            object extension;
            _extensions.TryGetValue(typeof(T), out extension);
            return extension;
        }

        public bool HasExtension(Type t)
        {
            if (typeof (IExtension).IsAssignableFrom(t))
            {
                return _extensions.ContainsKey(t);
            }
            return false;
        }

        public bool HasExtension<T>() where T : IExtension
        {
            return _extensions.ContainsKey(typeof (T));
        }

        /// <summary>
        ///     Configures the settings.
        /// </summary>
        /// <param name="config">The configuration.</param>
        private void ConfigureSettings(Config config)
        {
            Settings = new Settings(this, config);
        }

        /// <summary>
        ///     Configures the event stream.
        /// </summary>
        private void ConfigureEventStream()
        {
            EventStream = new EventStream(Settings.DebugEventStream);
            EventStream.StartStdoutLogger(Settings);
        }

        /// <summary>
        ///     Configures the serialization.
        /// </summary>
        private void ConfigureSerialization()
        {
            Serialization = new Serialization.Serialization(this);
        }

        /// <summary>
        ///     Configures the mailboxes.
        /// </summary>
        private void ConfigureMailboxes()
        {
            Mailboxes = new Mailboxes(this);
        }

        /// <summary>
        ///     Configures the provider.
        /// </summary>
        private void ConfigureProvider()
        {
            Type providerType = Type.GetType(Settings.ProviderClass);
            Debug.Assert(providerType != null, "providerType != null");
            var provider = (ActorRefProvider) Activator.CreateInstance(providerType, this);
            Provider = provider;
            Provider.Init();
        }

        /// <summary>
        ///     Starts this instance.
        /// </summary>
        private void Start()
        {
            if (Settings.LogDeadLetters > 0)
                logDeadLetterListener = SystemActorOf<DeadLetterListener>("deadLetterListener");
            

            if (Settings.LogConfigOnStart)
            {
                log.Warn(Settings.ToString());
            }
        }

        /// <summary>
        /// Extensions depends on loggers being configured before Start() is called
        /// </summary>
        private void ConfigureLoggers()
        {
            EventStream.StartDefaultLoggers(this);

            log = new BusLogging(EventStream, "ActorSystem(" + Name + ")", GetType());
        }

        /// <summary>
        ///     Configures the dispatchers.
        /// </summary>
        private void ConfigureDispatchers()
        {
            Dispatchers = new Dispatchers(this);
        }

        /// <summary>
        ///     Stop this actor system. This will stop the guardian actor, which in turn
        ///     will recursively stop all its child actors, then the system guardian
        ///     (below which the logging actors reside) and the execute all registered
        ///     termination handlers (<see cref="ActorSystem.RegisterOnTermination" />).
        /// </summary>
        public void Shutdown()
        {
            Provider.RootCell.Stop();
        }

        /// <summary>
        ///     Systems the actor of.
        /// </summary>
        /// <param name="props">The props.</param>
        /// <param name="name">The name.</param>
        /// <returns>InternalActorRef.</returns>
        public InternalActorRef SystemActorOf(Props props, string name = null)
        {
            return Provider.SystemGuardian.Cell.ActorOf(props, name);
        }

        /// <summary>
        ///     Systems the actor of.
        /// </summary>
        /// <typeparam name="TActor">The type of the t actor.</typeparam>
        /// <param name="name">The name.</param>
        /// <returns>InternalActorRef.</returns>
        public InternalActorRef SystemActorOf<TActor>(string name = null) where TActor : ActorBase
        {
            return Provider.SystemGuardian.Cell.ActorOf<TActor>(name);
        }

        public void Stop(ActorRef @ref)
        {
            if (@ref is LocalActorRef)
            {
                var l = @ref as LocalActorRef;
                l.Stop();
            }
        }

        #region Internal methods

        /// <summary>
        /// Used for seeding unique <see cref="Address"/> values upon Actor restarts; particularly important for remote Actors
        /// TODO: technically this feature belongs inside ActorSystem extensionsBase, but we don't have an implementation for that yet
        /// TODO: see https://github.com/akka/akka/blob/f1edf789798dc02dfa37d3301d7712736c964ab1/akka-remote/src/main/scala/akka/remote/AddressUidExtension.scala
        /// </summary>
        internal int AddressUid()
        {
            return ThreadLocalRandom.Current.Next();
        }

        #endregion

        #region Equality methods
        #endregion
    }
}