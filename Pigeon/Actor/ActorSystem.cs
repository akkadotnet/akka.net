using Pigeon.SignalR;
using Microsoft.AspNet.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pigeon.Actor;
using System.Collections.Concurrent;

namespace Pigeon.Actor
{
    public class ActorSystem : IActorRefFactory , IDisposable
    {
        private ActorCell rootCell;
        public ActorSystem()
        {            
            this.DefaultDispatcher = new ThreadPoolDispatcher();

            rootCell = new ActorCell(this);
            this.RootGuardian = rootCell.ActorOf<GuardianActor>("");

            this.EventStream = RootGuardian.Cell.ActorOf<EventStreamActor>("EventStream");
            this.DeadLetters = RootGuardian.Cell.ActorOf<DeadLettersActor>("deadLetters");
            this.Guardian = RootGuardian.Cell.ActorOf<GuardianActor>("user");
            this.SystemGuardian = RootGuardian.Cell.ActorOf<GuardianActor>("system");
            this.TempGuardian = RootGuardian.Cell.ActorOf<GuardianActor>("temp");
        }

        public LocalActorRef RootGuardian { get; private set; }

        public LocalActorRef EventStream { get; private set; }
        public LocalActorRef DeadLetters { get; private set; }
        public LocalActorRef Guardian { get; private set; }
        public LocalActorRef SystemGuardian { get; private set; }
        public LocalActorRef TempGuardian { get; private set; }

        public void Shutdown()
        {
            RootGuardian.Stop();
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
            return Guardian.Cell.ActorSelection(actorPath);
        }

        public ActorSelection ActorSelection(string actorPath)
        {
            return Guardian.Cell.ActorSelection(actorPath);
        }

        public MessageDispatcher DefaultDispatcher { get; set; }
    }
}