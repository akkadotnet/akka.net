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
    public abstract class ActorSystemExtension
    {
        public abstract void Start(ActorSystem system);
    }

    /// <summary>
    /// An actor system is a hierarchical group of actors which share common
    /// configuration, e.g. dispatchers, deployments, remote capabilities and
    /// addresses. It is also the entry point for creating or looking up actors.
    ///
    /// There are several possibilities for creating actors (see [[Akka.Actor.Props]]
    /// for details on `props`):
    ///
    /// <code>
    /// // C#
    /// system.ActorOf(props, "name");
    /// system.ActorOf(props);
    ///
    /// system.ActorOf(Props.Create(typeof(MyActor)), "name");
    /// system.ActorOf(Props.Create(() => new MyActor(arg1, arg2), "name");
    /// </code>
    ///
    /// Where no name is given explicitly, one will be automatically generated.
    ///
    /// <b><i>Important Notice:</i></b>
    ///
    /// This class is not meant to be extended by user code.
    /// </summary>
    public class ActorSystem : IActorRefFactory , IDisposable
    {
        
        public ActorRefProvider Provider { get; private set; }

        /// <summary>
        /// Creates a new ActorSystem with the specified name, and the specified Config
        /// </summary>
        /// <param name="name">Name of the ActorSystem</param>
        /// <param name="config">Configuration of the ActorSystem</param>
        /// <param name="extensions">Extensions of the ActorSystem</param>
        /// <returns></returns>
        public static ActorSystem Create(string name, Config config, params ActorSystemExtension[] extensions)
        {
            return new ActorSystem(name, config, extensions);
        }


        public static ActorSystem Create(string name, params ActorSystemExtension[] extensions)
        {
            return new ActorSystem(name, null, extensions);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static ActorSystem Create(string name)
        {
            return new ActorSystem(name, null);
        }

        private List<ActorSystemExtension> extensions = new List<ActorSystemExtension>();

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

        private void ConfigureScheduler()
        {
            this.Scheduler = new Scheduler();
        }

        private void ConfigureDeployer()
        {
            this.Deployer = new Deployer(this.Settings);
        }

        private void ConfigureExtensions(ActorSystemExtension[] extensions)
        {
            if (extensions != null)
            {
                this.extensions.AddRange(extensions);
                this.extensions.ForEach(e => e.Start(this));
            }
        }

        private void ConfigureSettings(Config config)
        {
            this.Settings = new Settings(this, config);
        }

        private void ConfigureEventStream()
        {
            this.EventStream = new EventStream(Settings.DebugEventStream);
            this.EventStream.StartStdoutLogger(Settings);
        }

        private void ConfigureSerialization()
        {
            this.Serialization = new Serialization.Serialization(this);
        }

        private void ConfigureMailboxes()
        {
            this.Mailboxes = new Mailboxes(this);
        }

        private void ConfigureProvider()
        {
            var providerType = Type.GetType(Settings.ProviderClass);
            var provider = (ActorRefProvider)Activator.CreateInstance(providerType, this);
            this.Provider = provider;
            this.Provider.Init();
        }

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

        private void ConfigureDispatchers()
        {
            this.Dispatchers = new Dispatchers(this);           
        }

        public Settings Settings { get;private set; }
        public string Name { get;private set; }
       
        public Serialization.Serialization Serialization { get;private set; }

        public LoggingAdapter log;
        private InternalActorRef logDeadLetterListener;

        /// <summary>
        /// Stop this actor system. This will stop the guardian actor, which in turn
        /// will recursively stop all its child actors, then the system guardian
        /// (below which the logging actors reside) and the execute all registered
        /// termination handlers (<see cref="ActorSystem.RegisterOnTermination"/>).
        /// </summary>
        public void Shutdown()
        {
            Provider.RootCell.Stop();
        }

        public void Dispose()
        {
            this.Shutdown();
        }

        public InternalActorRef SystemActorOf(Props props, string name = null)
        {
            return Provider.SystemGuardian.Cell.ActorOf(props, name);            
        }

        public InternalActorRef SystemActorOf<TActor>(string name = null) where TActor : ActorBase
        {
            return Provider.SystemGuardian.Cell.ActorOf<TActor>(name);
        }

        public InternalActorRef ActorOf(Props props, string name = null)
        {
            return Provider.Guardian.Cell.ActorOf(props, name);
        }

        public InternalActorRef ActorOf<TActor>(string name = null) where TActor : ActorBase
        {
            return Provider.Guardian.Cell.ActorOf<TActor>( name);
        }

        public ActorSelection ActorSelection(ActorPath actorPath)
        {
            return Provider.RootCell.ActorSelection(actorPath);
        }

        public ActorSelection ActorSelection(string actorPath)
        {
            return Provider.RootCell.ActorSelection(actorPath);
        }



        public EventStream EventStream { get; private set; }

        public ActorRef DeadLetters
        {
            get
            {
                return Provider.DeadLetters;
            }
        }

        public InternalActorRef Guardian
        {
            get
            {
                return this.Provider.Guardian ;
            }
        }

        public InternalActorRef SystemGuardian
        {
            get
            {
                return this.Provider.SystemGuardian;
            }
        }

        public Dispatchers Dispatchers { get;private set; }
        public Mailboxes Mailboxes { get;private set; }
        public Deployer Deployer { get;private set; }
        public Scheduler Scheduler { get;private set; }
    }
}