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
        private ActorCell rootCell;
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
            this.Provider = new ActorRefProvider();
            this.Settings = new Settings(this,config);
            this.Serialization = new Serialization.Serialization(this);
            ConfigDefaultDispatcher();
            this.Address = new Address("akka", this.Name); //TODO: this should not work this way...

            this.rootCell = new ActorCell(this,"",new ConcurrentQueueMailbox());
            this.EventStream = new EventBus();
            this.DeadLetters = rootCell.ActorOf<DeadLettersActor>("deadLetters");
            this.Guardian = rootCell.ActorOf<GuardianActor>("user");
            this.SystemGuardian = rootCell.ActorOf<GuardianActor>("system");
            this.TempGuardian = rootCell.ActorOf<GuardianActor>("temp");
            if (extensions != null)
            {
                this.extensions.AddRange(extensions);
                this.extensions.ForEach(e => e.Start(this));
            }
            this.Start();
        }

        private void Start()
        {
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
        public LocalActorRef RootGuardian { get; private set; }
        public EventBus EventStream { get; private set; }
        public LocalActorRef DeadLetters { get; private set; }
        public LocalActorRef Guardian { get; private set; }
        public LocalActorRef SystemGuardian { get; private set; }
        public LocalActorRef TempGuardian { get; private set; }
        public Serialization.Serialization Serialization { get;private set; }

        //TODO: read from config
        public LoggingAdapter log = new LoggingAdapter();

        public void Shutdown()
        {
            rootCell.Stop();
        }

        public void Dispose()
        {
            this.Shutdown();
        }

        public LocalActorRef ActorOf(Props props, string name = null)
        {
            return Guardian.Cell.ActorOf(props, name);
        }

        public LocalActorRef ActorOf<TActor>(string name = null) where TActor : ActorBase
        {
            return Guardian.Cell.ActorOf<TActor>( name);
        }

        public ActorSelection ActorSelection(ActorPath actorPath)
        {
            return rootCell.ActorSelection(actorPath);
        }

        public ActorSelection ActorSelection(string actorPath)
        {
            return rootCell.ActorSelection(actorPath);
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