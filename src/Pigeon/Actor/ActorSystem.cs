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

            this.Settings = new Settings(this,config);
            this.Serialization = new Serialization.Serialization(this);
            ConfigDefaultDispatcher();
            this.Address = new Address("akka", this.Name); //TODO: this should not work this way...

            this.Provider = new ActorRefProvider(this);
            this.Provider.Init();

            if (extensions != null)
            {
                this.extensions.AddRange(extensions);
                this.extensions.ForEach(e => e.Start(this));
            }
            this.Start();
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

        private void ConfigDefaultDispatcher()
        {
            this.DefaultDispatcher = new ThreadPoolDispatcher();
            this.DefaultDispatcher.Throughput = Settings.Config.GetInt("akka.actor.default-dispatcher.throughput", 100);
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

        public ActorSelection ActorSelection(ActorPath actorPath)
        {
            return Provider.RootCell.ActorSelection(actorPath);
        }

        public ActorSelection ActorSelection(string actorPath)
        {
            return Provider.RootCell.ActorSelection(actorPath);
        }

        public MessageDispatcher DefaultDispatcher { get; set; }

        public Func<ActorCell,ActorPath, ActorRef> ActorRefFactory { get; set; }
        internal protected virtual ActorRef GetRemoteRef(ActorCell actorCell, ActorPath actorPath)
        {
            if (ActorRefFactory == null)
                throw new NotImplementedException();

            return ActorRefFactory(actorCell,actorPath);
        }

        public virtual Address Address
        {
            get;
            set;
        }
    }
}