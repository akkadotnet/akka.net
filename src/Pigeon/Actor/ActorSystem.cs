using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pigeon.Actor;
using System.Collections.Concurrent;
using Pigeon.Dispatch;
using Pigeon.Configuration;
using Pigeon.Event;

namespace Pigeon.Actor
{
    public abstract class ActorSystemExtension
    {
        public abstract void Start(ActorSystem system);
    }

    public class ActorSystem : IActorRefFactory , IDisposable
    {
        
        public ActorRefProvider Provider { get; private set; }

        public static ActorSystem Create(string name, Config config, params ActorSystemExtension[] extensions)
        {
            return new ActorSystem(name, config, extensions);
        }

        public static ActorSystem Create(string name, params ActorSystemExtension[] extensions)
        {
            return new ActorSystem(name, null, extensions);
        }

        public static ActorSystem Create(string name)
        {
            return new ActorSystem(name, null);
        }

        private List<ActorSystemExtension> extensions = new List<ActorSystemExtension>();

        public ActorSystem(string name,Config config=null,params ActorSystemExtension[] extensions)
        {
            this.Name = name;
            
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
                this.logDeadLetterListener = this.Provider.SystemGuardian.Cell.ActorOf <DeadLetterListener>("deadLetterListener");

            if (Settings.LogConfigOnStart)
            {
                log.Info(Settings.ToString());
            }
        }

        private void ConfigureDispatchers()
        {
            this.Dispatchers = new Dispatchers(this);           
        }

        public Settings Settings { get;private set; }
        public string Name { get;private set; }
       
        public Serialization.Serialization Serialization { get;private set; }

        //TODO: read from config
        public LoggingAdapter log = new LoggingAdapter();
        private LocalActorRef logDeadLetterListener;

        public void Shutdown()
        {
            Provider.RootCell.Stop();
        }

        public void Dispose()
        {
            this.Shutdown();
        }

        public LocalActorRef ActorOf(Props props, string name = null)
        {
            return Provider.Guardian.Cell.ActorOf(props, name);
        }

        public LocalActorRef ActorOf<TActor>(string name = null) where TActor : ActorBase
        {
            return Provider.Guardian.Cell.ActorOf<TActor>( name);
        }

        public BrokenActorSelection ActorSelection(ActorPath actorPath)
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

        public LocalActorRef Guardian
        {
            get
            {
                return this.Provider.Guardian ;
            }
        }

        public LocalActorRef SystemGuardian
        {
            get
            {
                return this.Provider.SystemGuardian;
            }
        }

        public Dispatchers Dispatchers { get;private set; }
        public Mailboxes Mailboxes { get;private set; }
        public Deployer Deployer { get;private set; }
    }
}